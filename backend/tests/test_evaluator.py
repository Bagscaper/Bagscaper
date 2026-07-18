import asyncio
import logging
from datetime import timedelta
from types import SimpleNamespace

import pytest
from google.genai import errors

from app.ai.evaluator import GEMINI_RESPONSE_SCHEMA, GeminiSurvivalEvaluator, ResilientEvaluator
from app.core.observability import Metrics
from app.domain.preprocessor import build_evaluation_context, snapshot_session
from app.domain.rules import SURVIVAL_TYPE_RULE_CODES
from app.repositories.catalogs import load_item_catalog, load_survival_type_catalog
from app.repositories.sessions import SessionRepository
from app.schemas.game import GameStartRequest, SurvivalResponse
from app.schemas.survival import SurvivalTypeDefinition


def context():
    repository = SessionRepository(timedelta(hours=1))
    session = repository.create(GameStartRequest(gender="male", age_group="teen", disaster="flood"))
    survival_types = dict(load_survival_type_catalog())
    for code in SURVIVAL_TYPE_RULE_CODES - survival_types.keys():
        survival_types[code] = SurvivalTypeDefinition(type_code=code, name=f"type {code}", description="test type")
    return build_evaluation_context(snapshot_session(session), load_item_catalog(), survival_types)


class FakeModels:
    def __init__(self, outcomes) -> None:
        self.outcomes = list(outcomes)
        self.calls = 0
        self.kwargs = []

    async def generate_content(self, **kwargs):
        self.kwargs.append(kwargs)
        outcome = self.outcomes[self.calls]
        self.calls += 1
        if isinstance(outcome, BaseException):
            raise outcome
        return outcome


def fake_client(outcomes):
    return SimpleNamespace(models=FakeModels(outcomes))


async def test_retryable_timeout_is_retried_once(monkeypatch) -> None:
    ctx = context()
    parsed = SurvivalResponse(
        survival_type=ctx.mapped_survival_type,
        evaluation_narrative="재시도 뒤 구조화된 평가 응답을 반환했다.",
        survival_time_hours=ctx.rule_based_time_anchor_hours,
    )
    client = fake_client([asyncio.TimeoutError(), SimpleNamespace(parsed=parsed)])
    metrics = Metrics()
    evaluator = GeminiSurvivalEvaluator(client, "fake", timeout_seconds=1, retry_base_seconds=0, metrics=metrics)
    result = await evaluator.evaluate(ctx)
    assert result == parsed
    assert client.models.calls == 2
    assert metrics.counters["bagscape.ai.retry.total"] == 1
    call = client.models.kwargs[0]
    assert "additionalProperties" not in GEMINI_RESPONSE_SCHEMA
    assert call["config"].response_schema is not None
    assert call["config"].response_mime_type == "application/json"
    assert call["config"].system_instruction
    assert call["contents"].startswith("<context>")


async def test_dictionary_response_is_validated_as_survival_response() -> None:
    ctx = context()
    payload = {
        "survival_type": ctx.mapped_survival_type,
        "evaluation_narrative": "Gemini SDK가 dict로 파싱한 응답도 검증한다.",
        "survival_time_hours": ctx.rule_based_time_anchor_hours,
    }
    result = await GeminiSurvivalEvaluator(fake_client([SimpleNamespace(parsed=payload)]), "fake").evaluate(ctx)
    assert result == SurvivalResponse.model_validate(payload)


def retryable_errors() -> list[Exception]:
    return [
        errors.APIError(429, {"error": {"message": "limited"}}),
        errors.APIError(503, {"error": {"message": "server"}}),
    ]


@pytest.mark.parametrize("failure", retryable_errors(), ids=["rate-limit", "server-5xx"])
async def test_retryable_gemini_errors_retry_then_succeed(failure: Exception) -> None:
    ctx = context()
    parsed = SurvivalResponse(
        survival_type=ctx.mapped_survival_type,
        evaluation_narrative="일시 오류 재시도 뒤 정상 응답을 반환했다.",
        survival_time_hours=ctx.rule_based_time_anchor_hours,
    )
    client = fake_client([failure, SimpleNamespace(parsed=parsed)])
    evaluator = GeminiSurvivalEvaluator(client, "fake", retry_base_seconds=0)
    assert await evaluator.evaluate(ctx) == parsed
    assert client.models.calls == 2


async def test_non_retryable_4xx_fails_immediately() -> None:
    failure = errors.APIError(400, {"error": {"message": "bad request"}})
    client = fake_client([failure])
    with pytest.raises(errors.APIError):
        await GeminiSurvivalEvaluator(client, "fake", retry_base_seconds=0).evaluate(context())
    assert client.models.calls == 1


async def test_missing_parsed_response_falls_back_without_retry() -> None:
    ctx = context()
    client = fake_client([SimpleNamespace(parsed=None)])
    metrics = Metrics()
    evaluator = ResilientEvaluator(GeminiSurvivalEvaluator(client, "fake", retry_base_seconds=0), metrics=metrics)
    result = await evaluator.evaluate(ctx)
    assert client.models.calls == 1
    assert result.survival_type == ctx.mapped_survival_type
    assert metrics.counters["bagscape.ai.fallback.total"] == 1


async def test_exhausted_retries_increment_fallback_once_per_evaluation() -> None:
    ctx = context()
    client = fake_client([asyncio.TimeoutError(), asyncio.TimeoutError()])
    metrics = Metrics()
    primary = GeminiSurvivalEvaluator(client, "fake", max_attempts=2, retry_base_seconds=0, metrics=metrics)
    result = await ResilientEvaluator(primary, metrics=metrics).evaluate(ctx)
    assert result.survival_type == ctx.mapped_survival_type
    assert client.models.calls == 2
    assert metrics.counters["bagscape.ai.retry.total"] == 1
    assert metrics.counters["bagscape.ai.fallback.total"] == 1


async def test_missing_parsed_response_does_not_log_response_text(caplog) -> None:
    ctx = context()
    client = fake_client([SimpleNamespace(text="not available", parsed=None)])
    metrics = Metrics()
    primary = GeminiSurvivalEvaluator(client, "fake", retry_base_seconds=0)
    evaluator = ResilientEvaluator(primary, metrics=metrics)
    with caplog.at_level(logging.INFO):
        result = await evaluator.evaluate(ctx)
    assert client.models.calls == 1
    assert result.survival_type == ctx.mapped_survival_type
    assert metrics.counters["bagscape.ai.fallback.total"] == 1
    assert all("not available" not in record.getMessage() for record in caplog.records)
    assert all(hasattr(record, "request_id") for record in caplog.records)


async def test_invalid_survival_type_is_corrected() -> None:
    ctx = context()
    result = SurvivalResponse(
        survival_type="catalog 밖 칭호",
        evaluation_narrative="모델의 칭호만 서버가 결정론적으로 교정한다.",
        survival_time_hours=ctx.rule_based_time_anchor_hours,
    )
    metrics = Metrics()
    evaluator = ResilientEvaluator(
        GeminiSurvivalEvaluator(fake_client([SimpleNamespace(parsed=result)]), "fake"),
        metrics=metrics,
    )
    corrected = await evaluator.evaluate(ctx)
    assert corrected.survival_type == ctx.mapped_survival_type
    assert metrics.counters["bagscape.ai.survival_type_corrected.total"] == 1
