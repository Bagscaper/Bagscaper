from typing import Annotated

from pydantic import BaseModel, ConfigDict, Field, StringConstraints

from app.schemas.game import AgeGroup, DisasterType, Gender, NonBlankText

TypeCode = Annotated[str, StringConstraints(pattern=r"^[A-Za-z0-9_-]{1,32}$")]


class SurvivalTypeDefinition(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True, str_strip_whitespace=True)

    type_code: TypeCode
    name: NonBlankText = Field(max_length=80)
    description: NonBlankText = Field(max_length=500)


class SurvivalEvaluationContext(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True)

    gender: Gender
    age_group: AgeGroup
    disaster: DisasterType
    reference_bmr_kcal_day: int
    max_carry_weight_grams: int
    total_weight_grams: int
    excess_weight_grams: int
    is_overweight: bool
    required_water_72h_ml: int
    water_item_count: int = Field(ge=0)
    water_item_weight_grams: int = Field(ge=0)
    water_importance_total: int = Field(ge=0)
    importance_total: int = Field(ge=0)
    importance_average: float = Field(ge=0, le=5)
    important_item_count: int = Field(ge=0)
    category_counts: dict[str, int]
    category_weight_ratios: dict[str, float]
    disaster_relevant_count: int = Field(ge=0)
    irrelevant_count: int = Field(ge=0)
    selected_item_summary: list[str]
    balance_summary: str
    completion_score: int = Field(ge=0, le=100)
    rule_based_time_anchor_hours: int = Field(ge=0, le=72)
    action_count: int = Field(ge=0)
    inserted_then_removed_items: list[str]
    removed_critical_items: list[str]
    behavior_tags: list[str]
    mapped_survival_type_code: TypeCode
    mapped_survival_type: str
    mapped_survival_type_description: str
    candidate_survival_type_codes: list[TypeCode] = Field(min_length=1)
    candidate_survival_types: list[str] = Field(min_length=1)
