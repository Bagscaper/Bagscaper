from datetime import datetime, timezone
from uuid import UUID, uuid4

import pytest

from app.domain.preprocessor import SessionSnapshot, build_evaluation_context
from app.domain.rules import profile_for
from app.repositories.catalogs import load_item_catalog, load_survival_type_catalog
from app.repositories.sessions import ActionLogEntry
from app.schemas.game import GameLogRequest, GameStartRequest
from app.schemas.item import ItemDefinition

START = GameStartRequest(gender="female", age_group="age_20_30", disaster="fire")
ITEMS = load_item_catalog()
TYPES = load_survival_type_catalog()


def log(action: str, item_id: str, instance_id: UUID, *, applied: bool = True) -> ActionLogEntry:
    now = datetime.now(timezone.utc)
    request = GameLogRequest(
        session_id=uuid4(),
        action_id=uuid4(),
        item_instance_id=instance_id,
        action=action,
        item_id=item_id,
        occurred_at=now,
    )
    return ActionLogEntry(request=request, received_at=now, applied=applied)


def snapshot(inventory: tuple[tuple[UUID, str], ...] = (), logs: tuple[ActionLogEntry, ...] = ()) -> SessionSnapshot:
    return SessionSnapshot(START, inventory, logs)


def test_same_item_instances_are_counted_and_summarized_without_deduplication() -> None:
    first, second = uuid4(), uuid4()
    context = build_evaluation_context(snapshot(((first, "item_water"), (second, "item_water"))), ITEMS, TYPES)
    assert context.total_weight_grams == 4000
    assert context.water_item_count == 2
    assert context.importance_total == 10
    assert context.selected_item_summary == ["생수 x2"]


def test_behavior_is_instance_based_and_keeps_other_same_type_instance() -> None:
    removed, kept = uuid4(), uuid4()
    logs = (
        log("INSERT", "item_first_aid_kit", removed),
        log("INSERT", "item_first_aid_kit", kept),
        log("REMOVE", "item_first_aid_kit", removed),
    )
    context = build_evaluation_context(snapshot(((kept, "item_first_aid_kit"),), logs), ITEMS, TYPES)
    assert context.inserted_then_removed_items == ["구급상자, 상비약, 붕대 - AD 키트 1개"]
    assert context.removed_critical_items == ["구급상자, 상비약, 붕대 - AD 키트 1개"]
    assert "removed_critical_item" in context.behavior_tags


def test_no_op_removal_does_not_affect_behavior() -> None:
    context = build_evaluation_context(
        snapshot(logs=(log("REMOVE", "item_water", uuid4(), applied=False),)), ITEMS, TYPES
    )
    assert context.inserted_then_removed_items == []
    assert context.removed_critical_items == []


@pytest.mark.parametrize(("difference", "overweight"), [(-1, False), (0, False), (1, True)])
def test_weight_limit_uses_strict_excess_boundary(difference: int, overweight: bool) -> None:
    maximum = round(profile_for(START.age_group, START.gender).max_carry_weight_kg * 1000)
    item = ItemDefinition(
        item_id="boundary",
        name="boundary",
        category="도구",
        weight_kg=(maximum + difference) / 1000,
        importance_score=3,
        target_disaster="전체",
    )
    context = build_evaluation_context(snapshot(((uuid4(), item.item_id),)), {item.item_id: item}, TYPES)
    assert context.is_overweight is overweight
    assert context.excess_weight_grams == max(0, difference)


def test_mapping_uses_code_rules_and_display_catalog() -> None:
    context = build_evaluation_context(snapshot(), ITEMS, TYPES)
    assert context.mapped_survival_type_code == "DRSP"
    assert context.mapped_survival_type == TYPES["DRSP"].name
    assert context.candidate_survival_type_codes[-1] == "PRPS"
