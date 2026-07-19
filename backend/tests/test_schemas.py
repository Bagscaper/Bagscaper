import pytest
from pydantic import ValidationError

from app.schemas.game import GameLogRequest, GameResultRequest, GameStartRequest, SurvivalResponse


@pytest.mark.parametrize(
    ("model", "payload"),
    [
        (GameStartRequest, {"gender": "male", "age_group": "teen", "disaster": "fire", "extra": 1}),
        (GameResultRequest, {"session_id": "ed503034-9522-41f9-961c-a429796fcf51", "extra": 1}),
    ],
)
def test_all_request_schemas_forbid_extra_fields(model, payload) -> None:
    with pytest.raises(ValidationError):
        model.model_validate(payload)


def test_log_timestamp_requires_timezone() -> None:
    with pytest.raises(ValidationError):
        GameLogRequest.model_validate(
            {
                "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
                "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
                "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
                "action": "INSERT",
                "item_id": "item_water",
                "occurred_at": "2026-07-18T12:08:21",
            }
        )


def test_log_schema_forbids_extra_fields() -> None:
    with pytest.raises(ValidationError):
        GameLogRequest.model_validate(
            {
                "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
                "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
                "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
                "action": "INSERT",
                "item_id": "item_water",
                "occurred_at": "2026-07-18T12:08:21+09:00",
                "extra": True,
            }
        )


@pytest.mark.parametrize("occurred_at", ["2026-07-18T03:08:21Z", "2026-07-18T12:08:21+09:00"])
def test_log_timestamp_accepts_utc_and_explicit_offset(occurred_at: str) -> None:
    request = GameLogRequest.model_validate(
        {
            "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
            "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
            "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
            "action": "INSERT",
            "item_id": "item_water",
            "occurred_at": occurred_at,
        }
    )
    assert request.occurred_at.utcoffset() is not None


@pytest.mark.parametrize(
    ("field", "value"),
    [
        ("session_id", "not-a-uuid"),
        ("action_id", "not-a-uuid"),
        ("action", "PUT"),
        ("item_id", "contains space"),
        ("item_id", "x" * 65),
    ],
)
def test_log_rejects_invalid_identifiers_and_enum(field: str, value: str) -> None:
    payload = {
        "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
        "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
        "item_instance_id": "d8e2fd73-a12c-4e07-a282-3bc72f473b6d",
        "action": "INSERT",
        "item_id": "item_water",
        "occurred_at": "2026-07-18T03:08:21Z",
    }
    payload[field] = value
    with pytest.raises(ValidationError):
        GameLogRequest.model_validate(payload)


@pytest.mark.parametrize(
    "value",
    ["not-a-uuid", "6ba7b810-9dad-11d1-80b4-00c04fd430c8"],
)
def test_item_instance_id_requires_uuid4(value: str) -> None:
    payload = {
        "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
        "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
        "item_instance_id": value,
        "action": "INSERT",
        "item_id": "item_water",
        "occurred_at": "2026-07-18T03:08:21Z",
    }
    with pytest.raises(ValidationError):
        GameLogRequest.model_validate(payload)


def test_item_instance_id_is_required() -> None:
    with pytest.raises(ValidationError):
        GameLogRequest.model_validate(
            {
                "session_id": "ed503034-9522-41f9-961c-a429796fcf51",
                "action_id": "01e55799-7922-489d-b709-3ca91d1974e2",
                "action": "INSERT",
                "item_id": "item_water",
                "occurred_at": "2026-07-18T03:08:21Z",
            }
        )


@pytest.mark.parametrize("hours", [0, 72])
def test_survival_response_accepts_inclusive_hour_boundaries(hours: int) -> None:
    assert (
        SurvivalResponse(
            survival_type="유형", evaluation_narrative="평가", survival_time_hours=hours
        ).survival_time_hours
        == hours
    )


@pytest.mark.parametrize("hours", [-1, 73])
def test_survival_response_rejects_hours_outside_boundaries(hours: int) -> None:
    with pytest.raises(ValidationError):
        SurvivalResponse(survival_type="유형", evaluation_narrative="평가", survival_time_hours=hours)


@pytest.mark.parametrize(
    ("survival_type", "narrative"),
    [(" ", "평가"), ("유형", "\t"), ("가" * 81, "평가"), ("유형", "가" * 2001)],
)
def test_survival_response_rejects_blank_or_oversized_text(survival_type: str, narrative: str) -> None:
    with pytest.raises(ValidationError):
        SurvivalResponse(
            survival_type=survival_type,
            evaluation_narrative=narrative,
            survival_time_hours=1,
        )
