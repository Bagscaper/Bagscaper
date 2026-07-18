import asyncio
from typing import Mapping

from app.ai.evaluator import Evaluator
from app.core.errors import (
    action_id_conflict,
    inventory_limit,
    item_instance_conflict,
    log_limit_reached,
    result_timeout,
    session_completed,
    unknown_item,
)
from app.core.observability import Metrics
from app.domain.preprocessor import build_evaluation_context, snapshot_session
from app.domain.rules import profile_for, required_water_72h_ml
from app.repositories.sessions import ActionLogEntry, SessionRepository, SessionStatus, utcnow
from app.schemas.game import GameLogRequest, GameLogResponse, GameStartRequest, GameStartResponse, SurvivalResponse
from app.schemas.item import ItemDefinition
from app.schemas.survival import SurvivalTypeDefinition


class SurvivalService:
    def __init__(
        self,
        sessions: SessionRepository,
        items: Mapping[str, ItemDefinition],
        survival_types: Mapping[str, SurvivalTypeDefinition],
        evaluator: Evaluator,
        *,
        result_timeout_seconds: float = 10.0,
        metrics: Metrics | None = None,
    ) -> None:
        self.sessions = sessions
        self.items = items
        self.survival_types = survival_types
        self.evaluator = evaluator
        self.result_timeout_seconds = result_timeout_seconds
        self.metrics = metrics or Metrics()

    async def start_game(self, request: GameStartRequest) -> GameStartResponse:
        session = self.sessions.create(request)
        self.metrics.increment("bagscape.session.created")
        profile = profile_for(request.age_group, request.gender)
        return GameStartResponse(
            session_id=session.session_id,
            reference_bmr_kcal_day=profile.bmr_kcal_day,
            required_water_72h_ml=required_water_72h_ml(profile, request.disaster),
            max_carry_weight_kg=profile.max_carry_weight_kg,
            expires_at=session.expires_at,
        )

    async def log_action(self, request: GameLogRequest) -> GameLogResponse:
        if request.item_id not in self.items:
            self.metrics.increment("bagscape.game_log.unknown_item.total")
            raise unknown_item()
        session = self.sessions.get(request.session_id)
        async with session.lock:
            self.sessions.get(request.session_id)
            previous_request = session.action_requests.get(request.action_id)
            if previous_request is not None:
                if previous_request != request:
                    raise action_id_conflict()
                return session.action_responses[request.action_id]
            if session.status != SessionStatus.ACTIVE:
                raise session_completed()
            if len(session.action_logs) >= 500:
                raise log_limit_reached()

            existing_item_id = session.inventory.get(request.item_instance_id)
            if existing_item_id is not None and existing_item_id != request.item_id:
                self.metrics.increment("bagscape.game_log.item_instance_conflict.total")
                raise item_instance_conflict()

            if request.action.value == "INSERT":
                applied = existing_item_id is None
                if applied and len(session.inventory) >= 50:
                    self.metrics.increment("bagscape.game_log.inventory_limit.total")
                    raise inventory_limit()
            else:
                applied = existing_item_id == request.item_id

            # Reserve after all fallible checks so rejected actions cannot leave
            # partial state in the global idempotency registry.
            await self.sessions.claim_action_id(request)

            if request.action.value == "INSERT":
                if applied:
                    session.inventory[request.item_instance_id] = request.item_id
            else:
                if applied:
                    del session.inventory[request.item_instance_id]

            received_at = utcnow()
            session.action_logs.append(ActionLogEntry(request, received_at, applied))
            current_weight = sum(self.items[item_id].weight_grams for item_id in session.inventory.values())
            response = GameLogResponse(
                session_id=request.session_id,
                action_id=request.action_id,
                item_instance_id=request.item_instance_id,
                applied=applied,
                item_count=len(session.inventory),
                current_weight_grams=current_weight,
            )
            session.action_requests[request.action_id] = request
            session.action_responses[request.action_id] = response
            self.sessions.touch(session)
            return response

    async def create_result(self, session_id) -> SurvivalResponse:
        session = self.sessions.get(session_id)
        async with session.lock:
            self.sessions.get(session_id)
            if session.cached_result is not None:
                return session.cached_result
            if session.result_task is None:
                snapshot = session.result_snapshot or snapshot_session(session)
                session.result_snapshot = snapshot
                session.status = SessionStatus.RESULT_PENDING
                session.result_task = asyncio.create_task(self._generate_result(session, snapshot))
            task = session.result_task
        try:
            async with asyncio.timeout(self.result_timeout_seconds):
                return await asyncio.shield(task)
        except TimeoutError:
            raise result_timeout() from None

    async def _generate_result(self, session, snapshot) -> SurvivalResponse:
        try:
            context = build_evaluation_context(snapshot, self.items, self.survival_types)
            result = await self.evaluator.evaluate(context)
            async with session.lock:
                session.cached_result = result
                session.status = SessionStatus.COMPLETED
                self.sessions.touch(session)
            return result
        except Exception:
            async with session.lock:
                session.result_task = None
            raise
