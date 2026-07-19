from datetime import datetime, timedelta, timezone
from types import MappingProxyType
from uuid import UUID, uuid4

import pytest

from app.ai.evaluator import FallbackEvaluator
from app.core.errors import DomainError
from app.repositories.catalogs import load_survival_type_catalog
from app.repositories.sessions import SessionRepository, SessionStatus, utcnow
from app.schemas.game import GameLogRequest, GameStartRequest
from app.schemas.item import ItemDefinition
from app.services.survival import SurvivalService

START = GameStartRequest(gender="male", age_group="teen", disaster="earthquake")


def item_catalog(count: int = 51):
    return MappingProxyType(
        {
            f"item-{index}": ItemDefinition(
                item_id=f"item-{index}",
                name=f"item {index}",
                category="tool",
                weight_kg=(index + 1) / 1000,
                importance_score=3,
                target_disaster="전체",
            )
            for index in range(count)
        }
    )


def request(
    session_id: UUID,
    action: str,
    item_id: str,
    *,
    action_id: UUID | None = None,
    item_instance_id: UUID | None = None,
) -> GameLogRequest:
    return GameLogRequest(
        session_id=session_id,
        action_id=action_id or uuid4(),
        item_instance_id=item_instance_id or uuid4(),
        action=action,
        item_id=item_id,
        occurred_at=datetime.now(timezone.utc),
    )


def service(count: int = 51) -> tuple[SurvivalService, SessionRepository]:
    sessions = SessionRepository(timedelta(hours=2))
    return (
        SurvivalService(sessions, item_catalog(count), load_survival_type_catalog(), FallbackEvaluator()),
        sessions,
    )


async def test_insert_remove_and_opposite_actions_are_idempotent_no_ops() -> None:
    survival, sessions = service()
    started = await survival.start_game(START)
    instance_id = uuid4()
    inserted = await survival.log_action(request(started.session_id, "INSERT", "item-0", item_instance_id=instance_id))
    repeated = await survival.log_action(request(started.session_id, "INSERT", "item-0", item_instance_id=instance_id))
    removed = await survival.log_action(request(started.session_id, "REMOVE", "item-0", item_instance_id=instance_id))
    repeated_remove = await survival.log_action(
        request(started.session_id, "REMOVE", "item-0", item_instance_id=instance_id)
    )
    assert (inserted.applied, repeated.applied, removed.applied, repeated_remove.applied) == (True, False, True, False)
    assert sessions.get(started.session_id).inventory == {}
    assert len(sessions.get(started.session_id).action_logs) == 4


async def test_inventory_limit_accepts_fiftieth_and_rejects_fifty_first_atomically() -> None:
    survival, sessions = service()
    started = await survival.start_game(START)
    for index in range(50):
        await survival.log_action(request(started.session_id, "INSERT", f"item-{index}"))
    session = sessions.get(started.session_id)
    rejected = request(started.session_id, "INSERT", "item-50")
    expires_before = session.expires_at
    with pytest.raises(DomainError) as error:
        await survival.log_action(rejected)
    assert error.value.code == "INVENTORY_LIMIT"
    assert len(session.inventory) == 50
    assert len(session.action_logs) == 50
    assert rejected.action_id not in session.action_requests
    assert rejected.action_id not in sessions._action_registry
    assert session.expires_at == expires_before


async def test_log_limit_accepts_five_hundred_and_rejects_next_atomically() -> None:
    survival, sessions = service(1)
    started = await survival.start_game(START)
    instance_id = uuid4()
    for index in range(500):
        operation = "INSERT" if index % 2 == 0 else "REMOVE"
        await survival.log_action(request(started.session_id, operation, "item-0", item_instance_id=instance_id))
    session = sessions.get(started.session_id)
    rejected = request(started.session_id, "INSERT", "item-0")
    expires_before = session.expires_at
    with pytest.raises(DomainError) as error:
        await survival.log_action(rejected)
    assert error.value.code == "LOG_LIMIT_REACHED"
    assert len(session.action_logs) == 500
    assert rejected.action_id not in sessions._action_registry
    assert session.expires_at == expires_before


async def test_action_id_conflict_does_not_partially_change_session() -> None:
    survival, sessions = service(2)
    started = await survival.start_game(START)
    shared_id = uuid4()
    accepted = request(started.session_id, "INSERT", "item-0", action_id=shared_id)
    await survival.log_action(accepted)
    session = sessions.get(started.session_id)
    expires_before = session.expires_at
    with pytest.raises(DomainError) as error:
        await survival.log_action(request(started.session_id, "INSERT", "item-1", action_id=shared_id))
    assert error.value.code == "ACTION_ID_CONFLICT"
    assert session.inventory == {accepted.item_instance_id: "item-0"}
    assert len(session.action_logs) == 1
    assert session.expires_at == expires_before


async def test_action_id_comparison_includes_item_instance_id() -> None:
    survival, sessions = service(1)
    started = await survival.start_game(START)
    action_id = uuid4()
    accepted = request(started.session_id, "INSERT", "item-0", action_id=action_id)
    await survival.log_action(accepted)
    changed_instance = accepted.model_copy(update={"item_instance_id": uuid4()})
    with pytest.raises(DomainError) as error:
        await survival.log_action(changed_instance)
    assert error.value.code == "ACTION_ID_CONFLICT"
    assert sessions.get(started.session_id).inventory == {accepted.item_instance_id: "item-0"}


async def test_successful_action_extends_ttl() -> None:
    survival, sessions = service(1)
    started = await survival.start_game(START)
    session = sessions.get(started.session_id)
    session.expires_at = utcnow() + timedelta(seconds=1)
    previous = session.expires_at
    await survival.log_action(request(started.session_id, "INSERT", "item-0"))
    assert session.expires_at > previous


def test_cleanup_removes_expired_active_sessions_but_keeps_pending_and_registry_consistent() -> None:
    repository = SessionRepository(timedelta(hours=2))
    expired = repository.create(START)
    pending = repository.create(START)
    expired.expires_at = utcnow() - timedelta(seconds=1)
    pending.expires_at = utcnow() - timedelta(seconds=1)
    pending.status = SessionStatus.RESULT_PENDING
    action = request(expired.session_id, "INSERT", "item-0")
    repository._action_registry[action.action_id] = action
    assert repository.cleanup_expired() == 1
    assert expired.session_id not in repository._sessions
    assert expired.session_id in repository._expired_ids
    assert pending.session_id in repository._sessions
    assert action.action_id not in repository._action_registry


async def test_same_item_id_supports_multiple_instances_and_removes_exactly_one() -> None:
    survival, sessions = service(1)
    started = await survival.start_game(START)
    first, second = uuid4(), uuid4()
    one = await survival.log_action(request(started.session_id, "INSERT", "item-0", item_instance_id=first))
    two = await survival.log_action(request(started.session_id, "INSERT", "item-0", item_instance_id=second))
    assert one.current_weight_grams == 1
    assert two.current_weight_grams == 2
    await survival.log_action(request(started.session_id, "REMOVE", "item-0", item_instance_id=first))
    assert sessions.get(started.session_id).inventory == {second: "item-0"}


async def test_item_instance_conflict_is_atomic_and_counted() -> None:
    survival, sessions = service(2)
    started = await survival.start_game(START)
    instance_id = uuid4()
    await survival.log_action(request(started.session_id, "INSERT", "item-0", item_instance_id=instance_id))
    with pytest.raises(DomainError) as error:
        await survival.log_action(request(started.session_id, "REMOVE", "item-1", item_instance_id=instance_id))
    assert error.value.code == "ITEM_INSTANCE_CONFLICT"
    assert sessions.get(started.session_id).inventory == {instance_id: "item-0"}
    assert survival.metrics.counters["bagscape.game_log.item_instance_conflict.total"] == 1
