from collections import Counter, defaultdict
from dataclasses import dataclass
from typing import Mapping
from uuid import UUID

from app.domain.rules import DISASTER_DISPLAY_NAMES, profile_for, required_water_72h_ml
from app.repositories.sessions import ActionLogEntry, GameSession
from app.schemas.game import GameStartRequest, InventoryAction
from app.schemas.item import ItemDefinition
from app.schemas.survival import SurvivalEvaluationContext, SurvivalTypeDefinition

WATER_CATEGORIES = frozenset({"식수", "💧 식수"})


@dataclass(frozen=True)
class SessionSnapshot:
    start_request: GameStartRequest
    inventory_items: tuple[tuple[UUID, str], ...]
    action_logs: tuple[ActionLogEntry, ...]

    @property
    def item_ids(self) -> tuple[str, ...]:
        return tuple(item_id for _, item_id in self.inventory_items)


def snapshot_session(session: GameSession) -> SessionSnapshot:
    inventory_items = tuple(sorted(session.inventory.items(), key=lambda pair: str(pair[0])))
    return SessionSnapshot(session.start_request, inventory_items, tuple(session.action_logs))


def _summarize_item_ids(item_ids: list[str], items: Mapping[str, ItemDefinition]) -> list[str]:
    counts = Counter(item_ids)
    return [
        f"{items[item_id].name} x{count}" if count > 1 else items[item_id].name
        for item_id, count in sorted(counts.items())
    ]


def _behavior(
    logs: tuple[ActionLogEntry, ...],
    items: Mapping[str, ItemDefinition],
    final_inventory_instance_ids: set[UUID],
) -> tuple[list[str], list[str], list[str]]:
    inserted: dict[UUID, str] = {}
    removed_instances: dict[UUID, str] = {}
    removed_critical_instances: dict[UUID, str] = {}
    action_counts: Counter[UUID] = Counter()
    applied_insertions = 0

    for log in logs:
        instance_id = log.request.item_instance_id
        item_id = log.request.item_id
        action_counts[instance_id] += 1
        if not log.applied:
            continue
        if log.request.action == InventoryAction.INSERT:
            inserted[instance_id] = item_id
            applied_insertions += 1
        elif instance_id in inserted:
            removed_instances[instance_id] = item_id
            if items[item_id].importance_score >= 4:
                removed_critical_instances[instance_id] = item_id

    removed_item_ids = [
        item_id for instance_id, item_id in removed_instances.items() if instance_id not in final_inventory_instance_ids
    ]
    removed_critical_ids = [
        item_id
        for instance_id, item_id in removed_critical_instances.items()
        if instance_id not in final_inventory_instance_ids
    ]
    tags: set[str] = set()
    if removed_item_ids:
        tags.add("insert_then_remove")
    if removed_critical_ids:
        tags.add("removed_critical_item")
    if any(count >= 3 for count in action_counts.values()):
        tags.add("indecisive_selection")
    if applied_insertions >= 10:
        tags.add("many_insertions")
    return (
        _summarize_item_ids(removed_item_ids, items),
        _summarize_item_ids(removed_critical_ids, items),
        sorted(tags),
    )


def _type_codes(*, is_overweight: bool, removed_critical: bool, relevance_ratio: float, score: int) -> list[str]:
    ordered: list[str] = []
    if removed_critical:
        ordered.append("CRDP")
    if is_overweight:
        ordered.append("OWLP")
    if relevance_ratio < 0.5:
        ordered.append("DRSP")
    if score >= 65 and not is_overweight:
        ordered.append("SPSP")
    ordered.append("PRPS")
    return list(dict.fromkeys(ordered))


def build_evaluation_context(
    snapshot: SessionSnapshot,
    items: Mapping[str, ItemDefinition],
    survival_types: Mapping[str, SurvivalTypeDefinition],
) -> SurvivalEvaluationContext:
    start = snapshot.start_request
    profile = profile_for(start.age_group, start.gender)
    selected = [items[item_id] for item_id in snapshot.item_ids]
    total_weight = sum(item.weight_grams for item in selected)
    max_weight = round(profile.max_carry_weight_kg * 1000)
    excess = max(0, total_weight - max_weight)
    overweight = total_weight > max_weight

    category_counts = Counter(item.category for item in selected)
    category_weights: defaultdict[str, int] = defaultdict(int)
    for item in selected:
        category_weights[item.category] += item.weight_grams
    category_ratios = {
        category: round(weight / total_weight, 4) if total_weight else 0.0
        for category, weight in sorted(category_weights.items())
    }
    importance_total = sum(item.importance_score for item in selected)
    importance_average = importance_total / len(selected) if selected else 0.0
    important_count = sum(item.importance_score >= 4 for item in selected)
    water_items = [item for item in selected if item.category in WATER_CATEGORIES]

    disaster_name = DISASTER_DISPLAY_NAMES[start.disaster]
    relevant = [item for item in selected if "전체" in item.target_disaster or disaster_name in item.target_disaster]
    relevant_count = len(relevant)
    irrelevant_count = len(selected) - relevant_count
    relevance_ratio = relevant_count / len(selected) if selected else 0.0

    load_points = 20 if not overweight else round(20 * max(0, 1 - excess / max_weight))
    importance_points = round(30 * importance_average / 5)
    relevance_points = round(25 * relevance_ratio)
    diversity_points = min(15, len(category_counts) * 5)
    water_points = 10 if water_items else 0
    completion_score = max(
        0, min(100, load_points + importance_points + relevance_points + diversity_points + water_points)
    )
    hours = round(completion_score * 72 / 100)

    inserted_removed, removed_critical, behavior = _behavior(
        snapshot.action_logs, items, {instance_id for instance_id, _ in snapshot.inventory_items}
    )
    candidate_codes = _type_codes(
        is_overweight=overweight,
        removed_critical=bool(removed_critical),
        relevance_ratio=relevance_ratio,
        score=completion_score,
    )
    candidates = [survival_types[code] for code in candidate_codes]
    mapped = candidates[0]

    balance_parts: list[str] = []
    if overweight:
        balance_parts.append(f"중량 상한보다 {excess}g 초과")
    if not water_items:
        balance_parts.append("식수 카테고리 없음")
    if relevance_ratio < 0.5:
        balance_parts.append("현재 재난 관련 물품 비율 부족")
    if total_weight and max(category_ratios.values(), default=0) > 0.7:
        balance_parts.append("한 카테고리에 중량 편중")
    if not balance_parts:
        balance_parts.append("중량·카테고리·재난 적합도가 균형적")

    return SurvivalEvaluationContext(
        gender=start.gender,
        age_group=start.age_group,
        disaster=start.disaster,
        reference_bmr_kcal_day=profile.bmr_kcal_day,
        max_carry_weight_grams=max_weight,
        total_weight_grams=total_weight,
        excess_weight_grams=excess,
        is_overweight=overweight,
        required_water_72h_ml=required_water_72h_ml(profile, start.disaster),
        water_item_count=len(water_items),
        water_item_weight_grams=sum(item.weight_grams for item in water_items),
        water_importance_total=sum(item.importance_score for item in water_items),
        importance_total=importance_total,
        importance_average=round(importance_average, 4),
        important_item_count=important_count,
        category_counts=dict(sorted(category_counts.items())),
        category_weight_ratios=category_ratios,
        disaster_relevant_count=relevant_count,
        irrelevant_count=irrelevant_count,
        selected_item_summary=_summarize_item_ids(list(snapshot.item_ids), items),
        balance_summary="; ".join(balance_parts),
        completion_score=completion_score,
        rule_based_time_anchor_hours=hours,
        action_count=len(snapshot.action_logs),
        inserted_then_removed_items=inserted_removed,
        removed_critical_items=removed_critical,
        behavior_tags=behavior,
        mapped_survival_type_code=mapped.type_code,
        mapped_survival_type=mapped.name,
        mapped_survival_type_description=mapped.description,
        candidate_survival_type_codes=[entry.type_code for entry in candidates],
        candidate_survival_types=[entry.name for entry in candidates],
    )
