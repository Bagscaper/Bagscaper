import json
from pathlib import Path
from types import MappingProxyType
from typing import Mapping

from pydantic import TypeAdapter

from app.schemas.item import ItemDefinition
from app.schemas.survival import SurvivalTypeDefinition

ROOT = Path(__file__).resolve().parents[2]
ITEMS_PATH = ROOT / "items.json"
SURVIVAL_TYPES_PATH = ROOT / "survival_types.json"


def load_item_catalog(path: Path = ITEMS_PATH) -> Mapping[str, ItemDefinition]:
    items = TypeAdapter(list[ItemDefinition]).validate_python(json.loads(path.read_text(encoding="utf-8")))
    catalog = {item.item_id: item for item in items}
    if len(catalog) != len(items):
        raise ValueError("items.json contains duplicate item_id")
    return MappingProxyType(catalog)


def load_survival_type_catalog(path: Path = SURVIVAL_TYPES_PATH) -> Mapping[str, SurvivalTypeDefinition]:
    entries = TypeAdapter(list[SurvivalTypeDefinition]).validate_python(json.loads(path.read_text(encoding="utf-8")))
    catalog = {entry.type_code: entry for entry in entries}
    if len(catalog) != len(entries):
        raise ValueError("survival_types.json contains duplicate type_code")
    return MappingProxyType(catalog)
