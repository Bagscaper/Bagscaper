from datetime import datetime
from enum import StrEnum
from typing import Annotated
from uuid import UUID

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, field_validator

ItemId = Annotated[str, StringConstraints(pattern=r"^[A-Za-z0-9_-]{1,64}$")]
NonBlankText = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1)]


class Gender(StrEnum):
    MALE = "male"
    FEMALE = "female"
    OTHER = "other"


class AgeGroup(StrEnum):
    CHILD = "child"
    TEEN = "teen"
    AGE_20_30 = "age_20_30"
    AGE_40_50 = "age_40_50"
    AGE_60_PLUS = "age_60_plus"


class DisasterType(StrEnum):
    FIRE = "fire"
    FLOOD = "flood"
    TYPHOON = "typhoon"
    WILDFIRE = "wildfire"
    EARTHQUAKE = "earthquake"
    HEATWAVE = "heatwave"
    COLDWAVE = "coldwave"


class GameStartRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    gender: Gender
    age_group: AgeGroup
    disaster: DisasterType


class GameStartResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")
    session_id: UUID
    reference_bmr_kcal_day: int = Field(gt=0)
    required_water_72h_ml: int = Field(gt=0)
    max_carry_weight_kg: float = Field(gt=0)
    expires_at: datetime


class InventoryAction(StrEnum):
    INSERT = "INSERT"
    REMOVE = "REMOVE"


class GameLogRequest(BaseModel):
    model_config = ConfigDict(
        extra="forbid",
        json_schema_extra={
            "examples": [
                {
                    "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
                    "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
                    "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
                    "action": "INSERT",
                    "item_id": "item_water",
                    "occurred_at": "2026-07-18T12:08:21+09:00",
                }
            ]
        },
    )
    session_id: UUID
    action_id: UUID
    item_instance_id: UUID
    action: InventoryAction
    item_id: ItemId
    occurred_at: datetime

    @field_validator("occurred_at")
    @classmethod
    def require_timezone(cls, value: datetime) -> datetime:
        if value.tzinfo is None or value.utcoffset() is None:
            raise ValueError("occurred_at must include timezone")
        return value

    @field_validator("item_instance_id")
    @classmethod
    def require_uuid4(cls, value: UUID) -> UUID:
        if value.version != 4:
            raise ValueError("item_instance_id must be UUIDv4")
        return value


class GameLogResponse(BaseModel):
    model_config = ConfigDict(extra="forbid")
    session_id: UUID
    action_id: UUID
    item_instance_id: UUID
    applied: bool
    item_count: int = Field(ge=0, le=50)
    current_weight_grams: int = Field(ge=0)


class GameResultRequest(BaseModel):
    model_config = ConfigDict(extra="forbid")
    session_id: UUID


class SurvivalResponse(BaseModel):
    model_config = ConfigDict(extra="forbid", str_strip_whitespace=True)
    survival_type: NonBlankText = Field(max_length=80)
    evaluation_narrative: NonBlankText = Field(max_length=2_000)
    survival_time_hours: int = Field(ge=0, le=72)
