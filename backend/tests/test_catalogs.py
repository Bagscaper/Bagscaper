import json

import pytest
from pydantic import ValidationError

from app.repositories.catalogs import load_item_catalog, load_survival_type_catalog


def write_json(tmp_path, name: str, value):
    path = tmp_path / name
    path.write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")
    return path


def item(item_id: str = "water") -> dict:
    return {
        "item_id": item_id,
        "category": "💧 식수",
        "name": "물",
        "weight_kg": 0.5,
        "importance_score": 5,
        "target_disaster": "전체",
    }


def survival_type(code: str = "TEST") -> dict:
    return {"type_code": code, "name": f"type {code}", "description": "description"}


def test_catalogs_load_as_read_only_mappings() -> None:
    items = load_item_catalog()
    types = load_survival_type_catalog()
    assert items["item_water"].target_disaster == ("전체",)
    assert "SPSP" in types
    with pytest.raises(TypeError):
        items["new"] = items["item_water"]  # type: ignore[index]


def test_duplicate_ids_are_rejected(tmp_path) -> None:
    with pytest.raises(ValueError, match="duplicate item_id"):
        load_item_catalog(write_json(tmp_path, "items.json", [item("same"), item("same")]))
    with pytest.raises(ValueError, match="duplicate type_code"):
        load_survival_type_catalog(write_json(tmp_path, "types.json", [survival_type("SAME")] * 2))


@pytest.mark.parametrize("contents", ["", "{broken"])
def test_missing_or_invalid_catalog_file_fails(contents: str, tmp_path) -> None:
    path = tmp_path / "items.json"
    if contents:
        path.write_text(contents, encoding="utf-8")
    with pytest.raises((FileNotFoundError, json.JSONDecodeError)):
        load_item_catalog(path)


@pytest.mark.parametrize(
    "updates",
    [
        {"weight_kg": -0.1},
        {"importance_score": 6},
        {"category": " "},
        {"target_disaster": []},
        {"target_disaster": ["전체", "전체"]},
        {"target_disaster": ["전체", " "]},
        {"extra": True},
        {"weight_grams": 10},
    ],
)
def test_invalid_item_schema_is_rejected(updates: dict, tmp_path) -> None:
    value = item()
    value.update(updates)
    with pytest.raises(ValidationError):
        load_item_catalog(write_json(tmp_path, "items.json", [value]))


def test_target_disaster_union_normalizes_to_tuple(tmp_path) -> None:
    single = item("single")
    multiple = item("multiple")
    multiple["target_disaster"] = [" 전체 ", "화재"]
    catalog = load_item_catalog(write_json(tmp_path, "items.json", [single, multiple]))
    assert catalog["single"].target_disaster == ("전체",)
    assert catalog["multiple"].target_disaster == ("전체", "화재")


def test_survival_type_schema_forbids_legacy_and_extra_fields(tmp_path) -> None:
    bad = survival_type()
    bad["priority"] = 1
    with pytest.raises(ValidationError):
        load_survival_type_catalog(write_json(tmp_path, "types.json", [bad]))
