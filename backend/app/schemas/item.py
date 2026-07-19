from decimal import ROUND_HALF_UP, Decimal
from typing import Annotated, Any

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, field_validator

from app.schemas.game import ItemId, NonBlankText

Category = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=80)]


class ItemDefinition(BaseModel):
    model_config = ConfigDict(extra="forbid", frozen=True, str_strip_whitespace=True)

    item_id: ItemId
    category: Category
    name: NonBlankText = Field(max_length=80)
    weight_kg: float = Field(ge=0, le=50)
    importance_score: int = Field(ge=0, le=5)
    target_disaster: tuple[NonBlankText, ...] = Field(min_length=1)

    @field_validator("target_disaster", mode="before")
    @classmethod
    def normalize_target_disaster(cls, value: Any) -> Any:
        values = [value] if isinstance(value, str) else value
        if not isinstance(values, (list, tuple)):
            raise ValueError("target_disaster must be a string or string array")
        normalized = tuple(entry.strip() if isinstance(entry, str) else entry for entry in values)
        if not normalized or any(not isinstance(entry, str) or not entry for entry in normalized):
            raise ValueError("target_disaster must contain non-blank text")
        if len(normalized) != len(set(normalized)):
            raise ValueError("target_disaster must not contain duplicates")
        return normalized

    @property
    def weight_grams(self) -> int:
        """Return the catalog weight as an integer without binary-float accumulation."""
        return int((Decimal(str(self.weight_kg)) * 1000).quantize(Decimal("1"), rounding=ROUND_HALF_UP))
