import asyncio
import logging
import random
import time
from typing import Any, Protocol

import httpx
from google.genai import errors, types

from app.ai.prompts import build_user_prompt, render_system_prompt
from app.core.observability import Metrics, request_id_var
from app.schemas.game import SurvivalResponse
from app.schemas.survival import SurvivalEvaluationContext

logger = logging.getLogger(__name__)


def _gemini_response_schema() -> dict[str, Any]:
    """Return the Pydantic schema without keywords unsupported by Gemini."""

    def sanitize(value: Any) -> Any:
        if isinstance(value, dict):
            return {key: sanitize(nested) for key, nested in value.items() if key != "additionalProperties"}
        if isinstance(value, list):
            return [sanitize(nested) for nested in value]
        return value

    return sanitize(SurvivalResponse.model_json_schema(mode="serialization"))


GEMINI_RESPONSE_SCHEMA = _gemini_response_schema()


class Evaluator(Protocol):
    async def evaluate(self, context: SurvivalEvaluationContext) -> SurvivalResponse: ...


class AIResponseUnavailable(Exception):
    pass


class GeminiSurvivalEvaluator:
    def __init__(
        self,
        client: Any,
        model: str,
        max_concurrency: int = 8,
        *,
        timeout_seconds: float = 8.0,
        max_attempts: int = 2,
        retry_base_seconds: float = 0.25,
        retry_max_seconds: float = 1.0,
        metrics: Metrics | None = None,
    ) -> None:
        self.client = client
        self.model = model
        self._semaphore = asyncio.Semaphore(max_concurrency)
        self.timeout_seconds = timeout_seconds
        self.max_attempts = max_attempts
        self.retry_base_seconds = retry_base_seconds
        self.retry_max_seconds = retry_max_seconds
        self.metrics = metrics or Metrics()

    @staticmethod
    def _retryable(exc: Exception) -> bool:
        if isinstance(exc, (asyncio.TimeoutError, httpx.TransportError)):
            return True
        return isinstance(exc, errors.APIError) and (exc.code in {408, 429} or exc.code >= 500)

    async def evaluate(self, context: SurvivalEvaluationContext) -> SurvivalResponse:
        async with self._semaphore:
            for attempt in range(1, self.max_attempts + 1):
                started = time.perf_counter()
                try:
                    response = await asyncio.wait_for(
                        self.client.models.generate_content(
                            model=self.model,
                            contents=build_user_prompt(context),
                            config=types.GenerateContentConfig(
                                system_instruction=render_system_prompt(),
                                response_mime_type="application/json",
                                response_schema=GEMINI_RESPONSE_SCHEMA,
                            ),
                        ),
                        timeout=self.timeout_seconds,
                    )
                    if response.parsed is None:
                        raise AIResponseUnavailable("missing parsed response")
                    parsed = (
                        response.parsed
                        if isinstance(response.parsed, SurvivalResponse)
                        else SurvivalResponse.model_validate(response.parsed)
                    )
                    self.metrics.observe("bagscape.ai.request.duration", time.perf_counter() - started)
                    logger.info(
                        "AI evaluation completed",
                        extra={
                            "request_id": request_id_var.get(),
                            "model": self.model,
                            "ai_attempt": attempt,
                            "ai_outcome": "success",
                        },
                    )
                    return parsed
                except asyncio.TimeoutError as exc:
                    error: Exception = exc
                except Exception as exc:
                    error = exc

                self.metrics.observe("bagscape.ai.request.duration", time.perf_counter() - started)
                if not self._retryable(error) or attempt == self.max_attempts:
                    logger.warning(
                        "AI evaluation attempt failed",
                        extra={
                            "request_id": request_id_var.get(),
                            "model": self.model,
                            "ai_attempt": attempt,
                            "ai_outcome": "failed",
                            "fallback_reason": type(error).__name__,
                        },
                    )
                    raise error
                self.metrics.increment("bagscape.ai.retry.total")
                logger.warning(
                    "AI evaluation will retry",
                    extra={
                        "request_id": request_id_var.get(),
                        "model": self.model,
                        "ai_attempt": attempt,
                        "ai_outcome": "retry",
                        "fallback_reason": type(error).__name__,
                    },
                )
                delay_cap = min(self.retry_max_seconds, self.retry_base_seconds * (2 ** (attempt - 1)))
                if delay_cap:
                    await asyncio.sleep(random.uniform(0, delay_cap))
        raise AIResponseUnavailable("evaluation did not complete")


class FallbackEvaluator:
    async def evaluate(self, context: SurvivalEvaluationContext) -> SurvivalResponse:
        return fallback_response(context)


def fallback_response(context: SurvivalEvaluationContext) -> SurvivalResponse:
    water = (
        f"72시간 필요 수분 기준은 {context.required_water_72h_ml}ml이며, 식수 카테고리 물품 "
        f"{context.water_item_count}개({context.water_item_weight_grams}g)를 선택했다"
        if context.water_item_count
        else f"72시간 필요 수분 기준은 {context.required_water_72h_ml}ml이나 식수 카테고리 물품이 없다"
    )
    importance = (
        f"중요도 합계는 {context.importance_total}, 평균은 {context.importance_average:.2f}이며 "
        f"고중요도 물품은 {context.important_item_count}개다"
    )
    relevance = (
        f"현재 재난 관련 물품은 {context.disaster_relevant_count}개이고 관련성이 낮은 물품은 "
        f"{context.irrelevant_count}개다"
    )
    selected = ", ".join(context.selected_item_summary) if context.selected_item_summary else "선택한 물품 없음"
    behavior = "행동 기록에서 특별한 선택 번복은 없었다"
    if context.inserted_then_removed_items:
        behavior = f"넣었다가 뺀 물품({', '.join(context.inserted_then_removed_items)})이 있었다"
    if context.removed_critical_items:
        behavior += f"; 특히 중요 물품({', '.join(context.removed_critical_items)})을 제거했다"
    narrative = (
        f"{water}. 선택 물품은 {selected}이다. {importance}. {relevance}. "
        f"전체 가방은 {context.balance_summary} 상태다. {behavior}. 이 구성을 종합한 규칙 기반 "
        f"예상 생존 시간은 {context.rule_based_time_anchor_hours}시간이다."
    )
    return SurvivalResponse(
        survival_type=context.mapped_survival_type,
        evaluation_narrative=narrative,
        survival_time_hours=context.rule_based_time_anchor_hours,
    )


class ResilientEvaluator:
    def __init__(
        self,
        primary: Evaluator | None,
        *,
        timeout_seconds: float = 10.0,
        metrics: Metrics | None = None,
    ) -> None:
        self.primary = primary
        self.timeout_seconds = timeout_seconds
        self.metrics = metrics or Metrics()

    async def evaluate(self, context: SurvivalEvaluationContext) -> SurvivalResponse:
        if self.primary is None:
            self.metrics.increment("bagscape.ai.fallback.total")
            logger.info(
                "Deterministic evaluation used because AI is disabled",
                extra={
                    "request_id": request_id_var.get(),
                    "ai_outcome": "fallback",
                    "fallback_reason": "ai_disabled",
                    "rule_based_time_anchor_hours": context.rule_based_time_anchor_hours,
                },
            )
            return fallback_response(context)
        try:
            async with asyncio.timeout(self.timeout_seconds):
                result = await self.primary.evaluate(context)
        except asyncio.CancelledError:
            raise
        except Exception as exc:
            self.metrics.increment("bagscape.ai.fallback.total")
            logger.warning(
                "AI evaluation failed; deterministic fallback used",
                extra={
                    "request_id": request_id_var.get(),
                    "ai_outcome": "fallback",
                    "fallback_reason": type(exc).__name__,
                    "rule_based_time_anchor_hours": context.rule_based_time_anchor_hours,
                },
            )
            return fallback_response(context)
        if result.survival_type not in context.candidate_survival_types:
            self.metrics.increment("bagscape.ai.survival_type_corrected.total")
            logger.info(
                "AI survival type corrected",
                extra={
                    "request_id": request_id_var.get(),
                    "ai_outcome": "corrected",
                    "survival_type_corrected": True,
                    "rule_based_time_anchor_hours": context.rule_based_time_anchor_hours,
                },
            )
            return result.model_copy(update={"survival_type": context.mapped_survival_type})
        return result
