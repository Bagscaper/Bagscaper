import asyncio
from datetime import timedelta
from pathlib import Path
from types import MappingProxyType
from uuid import UUID, uuid4

import pytest
from fastapi.testclient import TestClient

from app.core.config import Settings
from app.main import create_app
from app.repositories.sessions import utcnow
from app.schemas.game import SurvivalResponse
from app.schemas.item import ItemDefinition


def action(session_id: str, action_id: str, operation: str, item_id: str, item_instance_id: str | None = None) -> dict:
    return {
        "session_id": session_id,
        "action_id": action_id,
        "item_instance_id": item_instance_id or str(uuid4()),
        "action": operation,
        "item_id": item_id,
        "occurred_at": "2026-07-18T12:08:21+09:00",
    }


def test_full_game_loop_and_idempotency() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        assert client.get("/health/live").json() == {"status": "ok"}
        assert client.get("/health/ready").status_code == 200
        start = client.post("/game/start", json={"gender": "female", "age_group": "age_20_30", "disaster": "wildfire"})
        assert start.status_code == 201
        started = start.json()
        assert started["reference_bmr_kcal_day"] == 1350
        assert started["required_water_72h_ml"] == 6930
        assert started["max_carry_weight_kg"] == 11.0

        action_id = str(uuid4())
        payload = action(started["session_id"], action_id, "INSERT", "item_water")
        first = client.post("/game/log", json=payload)
        repeated = client.post("/game/log", json=payload)
        assert first.status_code == 200
        assert repeated.json() == first.json()
        assert first.json()["item_instance_id"] == payload["item_instance_id"]
        assert first.json()["current_weight_grams"] == 2000

        conflict_payload = action(started["session_id"], action_id, "REMOVE", "item_water")
        conflict = client.post("/game/log", json=conflict_payload)
        assert conflict.status_code == 409
        assert conflict.json()["error"]["code"] == "ACTION_ID_CONFLICT"

        result = client.post("/game/result", json={"session_id": started["session_id"]})
        cached = client.post("/game/result", json={"session_id": started["session_id"]})
        assert result.status_code == 200
        assert cached.json() == result.json()
        assert set(result.json()) == {"survival_type", "evaluation_narrative", "survival_time_hours"}
        assert 0 <= result.json()["survival_time_hours"] <= 72
        assert "6930ml" in result.json()["evaluation_narrative"]

        after_result = client.post(
            "/game/log", json=action(started["session_id"], str(uuid4()), "INSERT", "item_flashlight")
        )
        assert after_result.status_code == 409
        assert after_result.json()["error"]["code"] == "SESSION_COMPLETED"


def test_unknown_item_and_missing_session_errors() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        start = client.post("/game/start", json={"gender": "male", "age_group": "teen", "disaster": "flood"}).json()
        unknown = client.post("/game/log", json=action(start["session_id"], str(uuid4()), "INSERT", "not-in-catalog"))
        assert unknown.status_code == 422
        assert unknown.json() == {
            "error": {"code": "UNKNOWN_ITEM", "message": "Item does not exist in the server catalog"}
        }
        missing = client.post("/game/result", json={"session_id": str(uuid4())})
        assert missing.status_code == 404
        assert missing.json()["error"]["code"] == "SESSION_NOT_FOUND"


def test_validation_errors_use_fastapi_422() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        response = client.post("/game/start", json={"gender": "female", "age_group": "unknown", "disaster": "fire"})
        assert response.status_code == 422
        assert response.json()["error"]["code"] == "VALIDATION_ERROR"


def test_request_id_is_preserved_only_when_valid() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        supplied = str(uuid4())
        valid = client.get("/health/live", headers={"X-Request-ID": supplied})
        invalid = client.get("/health/live", headers={"X-Request-ID": "not-a-uuid"})
        assert valid.headers["X-Request-ID"] == supplied
        assert invalid.headers["X-Request-ID"] != "not-a-uuid"
        assert len(invalid.headers["X-Request-ID"]) == 36


def test_action_id_cannot_be_reused_across_sessions() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        body = {"gender": "female", "age_group": "age_20_30", "disaster": "fire"}
        first_session = client.post("/game/start", json=body).json()["session_id"]
        second_session = client.post("/game/start", json=body).json()["session_id"]
        shared_action_id = str(uuid4())
        assert (
            client.post("/game/log", json=action(first_session, shared_action_id, "INSERT", "item_water")).status_code
            == 200
        )
        conflict = client.post("/game/log", json=action(second_session, shared_action_id, "INSERT", "item_water"))
        assert conflict.status_code == 409
        assert conflict.json()["error"]["code"] == "ACTION_ID_CONFLICT"


def test_catalogs_are_loaded_once_during_startup(monkeypatch) -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:

        def fail_read(*_args, **_kwargs):
            raise AssertionError("catalogs must not be read while handling requests")

        monkeypatch.setattr(Path, "read_text", fail_read)
        started = client.post("/game/start", json={"gender": "male", "age_group": "teen", "disaster": "fire"})
        assert started.status_code == 201
        logged = client.post(
            "/game/log", json=action(started.json()["session_id"], str(uuid4()), "INSERT", "item_water")
        )
        assert logged.status_code == 200
        assert client.post("/game/result", json={"session_id": started.json()["session_id"]}).status_code == 200


def test_expired_session_error_and_request_id() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        started = client.post("/game/start", json={"gender": "male", "age_group": "teen", "disaster": "fire"}).json()
        app.state.sessions.get(UUID(started["session_id"])).expires_at = utcnow() - timedelta(seconds=1)
        response = client.post("/game/result", json={"session_id": started["session_id"]})
        assert response.status_code == 410
        assert response.json()["error"]["code"] == "SESSION_EXPIRED"
        assert len(response.headers["X-Request-ID"]) == 36


def large_catalog():
    return MappingProxyType(
        {
            f"item-{index}": ItemDefinition(
                item_id=f"item-{index}",
                name=f"item {index}",
                category="tool",
                weight_kg=0.001,
                importance_score=1,
                target_disaster="전체",
            )
            for index in range(51)
        }
    )


def test_inventory_and_log_limit_error_envelopes(monkeypatch) -> None:
    monkeypatch.setattr("app.main.load_item_catalog", large_catalog)
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        body = {"gender": "male", "age_group": "teen", "disaster": "fire"}
        inventory_session = client.post("/game/start", json=body).json()["session_id"]
        for index in range(50):
            assert (
                client.post(
                    "/game/log", json=action(inventory_session, str(uuid4()), "INSERT", f"item-{index}")
                ).status_code
                == 200
            )
        limited = client.post("/game/log", json=action(inventory_session, str(uuid4()), "INSERT", "item-50"))
        assert limited.status_code == 409
        assert limited.json()["error"]["code"] == "INVENTORY_LIMIT"

        log_session = client.post("/game/start", json=body).json()["session_id"]
        app.state.sessions.get(UUID(log_session)).action_logs = [object()] * 500
        limited = client.post("/game/log", json=action(log_session, str(uuid4()), "INSERT", "item-0"))
        assert limited.status_code == 429
        assert limited.json()["error"]["code"] == "LOG_LIMIT_REACHED"


def test_instance_conflict_error_and_metrics() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        started = client.post("/game/start", json={"gender": "male", "age_group": "teen", "disaster": "fire"}).json()
        instance_id = str(uuid4())
        accepted = client.post(
            "/game/log", json=action(started["session_id"], str(uuid4()), "INSERT", "item_water", instance_id)
        )
        conflict = client.post(
            "/game/log", json=action(started["session_id"], str(uuid4()), "INSERT", "item_flashlight", instance_id)
        )
        assert accepted.status_code == 200
        assert conflict.status_code == 409
        assert conflict.json()["error"]["code"] == "ITEM_INSTANCE_CONFLICT"
        assert app.state.metrics.counters["bagscape.game_log.item_instance_conflict.total"] == 1


def test_openapi_documents_dynamic_instance_contract() -> None:
    schema = create_app(Settings(gemini_api_key=None)).openapi()
    request_schema = schema["components"]["schemas"]["GameLogRequest"]
    response_schema = schema["components"]["schemas"]["GameLogResponse"]
    assert "item_instance_id" in request_schema["required"]
    assert "item_instance_id" in response_schema["required"]
    examples = schema["paths"]["/game/log"]["post"]["responses"]["409"]["content"]["application/json"]["examples"]
    assert examples["item_instance_conflict"]["value"]["error"]["code"] == "ITEM_INSTANCE_CONFLICT"


class SlowEvaluator:
    async def evaluate(self, context) -> SurvivalResponse:
        await asyncio.sleep(0.02)
        return SurvivalResponse(
            survival_type=context.mapped_survival_type,
            evaluation_narrative="시간 초과 뒤 재요청에서 공유 작업을 완료했다.",
            survival_time_hours=context.rule_based_time_anchor_hours,
        )


def test_result_timeout_envelope_can_be_retried() -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        started = client.post("/game/start", json={"gender": "male", "age_group": "teen", "disaster": "fire"}).json()
        app.state.service.evaluator = SlowEvaluator()
        app.state.service.result_timeout_seconds = 0.001
        timed_out = client.post("/game/result", json={"session_id": started["session_id"]})
        assert timed_out.status_code == 504
        assert timed_out.json()["error"]["code"] == "RESULT_TIMEOUT"
        app.state.service.result_timeout_seconds = 1
        retried = client.post("/game/result", json={"session_id": started["session_id"]})
        assert retried.status_code == 200
        assert retried.json()["evaluation_narrative"].startswith("시간 초과")


@pytest.mark.parametrize(
    ("path", "payload"),
    [
        ("/game/start", {"gender": "invalid", "age_group": "teen", "disaster": "fire"}),
        ("/game/result", {"session_id": str(uuid4())}),
    ],
)
def test_error_responses_always_include_request_id(path: str, payload: dict) -> None:
    app = create_app(Settings(gemini_api_key=None))
    with TestClient(app) as client:
        response = client.post(path, json=payload)
        assert response.status_code in {404, 422}
        assert len(response.headers["X-Request-ID"]) == 36
