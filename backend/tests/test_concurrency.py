import asyncio
from contextlib import suppress
from datetime import timedelta
from uuid import uuid4

import pytest

from app.core.errors import DomainError
from app.repositories.catalogs import load_item_catalog, load_survival_type_catalog
from app.repositories.sessions import SessionRepository, SessionStatus
from app.schemas.game import GameLogRequest, GameStartRequest, SurvivalResponse
from app.services.survival import SurvivalService

START = GameStartRequest(gender="female", age_group="age_20_30", disaster="fire")


class GateEvaluator:
    def __init__(self) -> None:
        self.calls = 0
        self.started = asyncio.Event()
        self.release = asyncio.Event()

    async def evaluate(self, context) -> SurvivalResponse:
        self.calls += 1
        self.started.set()
        await self.release.wait()
        return SurvivalResponse(
            survival_type=context.mapped_survival_type,
            evaluation_narrative="취소된 HTTP 대기자와 무관하게 공유 평가 작업을 완료했다.",
            survival_time_hours=context.rule_based_time_anchor_hours,
        )


async def test_cancelled_waiter_does_not_cancel_shared_result() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    evaluator = GateEvaluator()
    service = SurvivalService(sessions, load_item_catalog(), load_survival_type_catalog(), evaluator)
    started = await service.start_game(START)

    first = asyncio.create_task(service.create_result(started.session_id))
    await evaluator.started.wait()
    first.cancel()
    with suppress(asyncio.CancelledError):
        await first

    evaluator.release.set()
    result = await service.create_result(started.session_id)
    assert evaluator.calls == 1
    assert result.evaluation_narrative.startswith("취소된 HTTP")
    assert sessions.get(started.session_id).status == SessionStatus.COMPLETED


async def test_result_request_timeout_keeps_pending_task_for_retry() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    evaluator = GateEvaluator()
    service = SurvivalService(
        sessions,
        load_item_catalog(),
        load_survival_type_catalog(),
        evaluator,
        result_timeout_seconds=0.01,
    )
    started = await service.start_game(START)

    with pytest.raises(DomainError) as error:
        await service.create_result(started.session_id)
    assert error.value.code == "RESULT_TIMEOUT"
    assert sessions.get(started.session_id).status == SessionStatus.RESULT_PENDING

    evaluator.release.set()
    await asyncio.sleep(0)
    result = await service.create_result(started.session_id)
    assert evaluator.calls == 1
    assert result.survival_time_hours >= 0


async def test_repository_close_cancels_pending_results() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    evaluator = GateEvaluator()
    service = SurvivalService(sessions, load_item_catalog(), load_survival_type_catalog(), evaluator)
    started = await service.start_game(START)
    waiter = asyncio.create_task(service.create_result(started.session_id))
    await evaluator.started.wait()
    await sessions.close()
    with suppress(asyncio.CancelledError):
        await waiter
    assert sessions.get(started.session_id).result_task.cancelled()


def action(session_id, action_id, item_id) -> GameLogRequest:
    from datetime import datetime, timezone

    return GameLogRequest(
        session_id=session_id,
        action_id=action_id,
        item_instance_id=uuid4(),
        action="INSERT",
        item_id=item_id,
        occurred_at=datetime.now(timezone.utc),
    )


async def test_concurrent_distinct_inserts_are_both_applied_atomically() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    service = SurvivalService(sessions, load_item_catalog(), load_survival_type_catalog(), GateEvaluator())
    started = await service.start_game(START)
    first, second = await asyncio.gather(
        service.log_action(action(started.session_id, uuid4(), "item_water")),
        service.log_action(action(started.session_id, uuid4(), "item_flashlight")),
    )
    assert first.applied and second.applied
    assert set(sessions.get(started.session_id).inventory.values()) == {"item_water", "item_flashlight"}
    assert len(sessions.get(started.session_id).action_logs) == 2


async def test_concurrent_duplicate_action_id_returns_one_shared_effect() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    service = SurvivalService(sessions, load_item_catalog(), load_survival_type_catalog(), GateEvaluator())
    started = await service.start_game(START)
    payload = action(started.session_id, uuid4(), "item_water")
    first, second = await asyncio.gather(service.log_action(payload), service.log_action(payload))
    assert first == second
    assert len(sessions.get(started.session_id).action_logs) == 1


class CapturingEvaluator:
    def __init__(self) -> None:
        self.contexts = []

    async def evaluate(self, context) -> SurvivalResponse:
        self.contexts.append(context)
        return SurvivalResponse(
            survival_type=context.mapped_survival_type,
            evaluation_narrative="잠금 획득 순서에 맞춰 확정된 스냅샷을 평가했다.",
            survival_time_hours=context.rule_based_time_anchor_hours,
        )


async def test_log_queued_before_result_is_included_in_snapshot() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    evaluator = CapturingEvaluator()
    service = SurvivalService(sessions, load_item_catalog(), load_survival_type_catalog(), evaluator)
    started = await service.start_game(START)
    session = sessions.get(started.session_id)
    await session.lock.acquire()
    log_task = asyncio.create_task(service.log_action(action(started.session_id, uuid4(), "item_water")))
    await asyncio.sleep(0)
    result_task = asyncio.create_task(service.create_result(started.session_id))
    session.lock.release()
    await asyncio.gather(log_task, result_task)
    assert evaluator.contexts[0].water_item_count == 1
    assert evaluator.contexts[0].action_count == 1


async def test_result_queued_before_log_freezes_snapshot_and_rejects_log() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    evaluator = CapturingEvaluator()
    service = SurvivalService(sessions, load_item_catalog(), load_survival_type_catalog(), evaluator)
    started = await service.start_game(START)
    session = sessions.get(started.session_id)
    await session.lock.acquire()
    result_task = asyncio.create_task(service.create_result(started.session_id))
    await asyncio.sleep(0)
    log_task = asyncio.create_task(service.log_action(action(started.session_id, uuid4(), "item_water")))
    session.lock.release()
    await result_task
    with pytest.raises(DomainError) as error:
        await log_task
    assert error.value.code == "SESSION_COMPLETED"
    assert evaluator.contexts[0].water_item_count == 0
    assert evaluator.contexts[0].action_count == 0
