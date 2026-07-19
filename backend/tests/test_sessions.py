import asyncio
from datetime import timedelta

import pytest

from app.core.errors import DomainError
from app.repositories.catalogs import load_item_catalog, load_survival_type_catalog
from app.repositories.sessions import SessionRepository
from app.schemas.game import GameStartRequest, SurvivalResponse
from app.services.survival import SurvivalService

START = GameStartRequest(gender="male", age_group="teen", disaster="earthquake")


def test_expired_session_returns_gone() -> None:
    repository = SessionRepository(timedelta(seconds=-1))
    session = repository.create(START)
    with pytest.raises(DomainError) as error:
        repository.get(session.session_id)
    assert error.value.status_code == 410
    assert error.value.code == "SESSION_EXPIRED"


class CountingEvaluator:
    def __init__(self) -> None:
        self.calls = 0

    async def evaluate(self, context) -> SurvivalResponse:
        self.calls += 1
        await asyncio.sleep(0.02)
        return SurvivalResponse(
            survival_type=context.mapped_survival_type,
            evaluation_narrative="동시 요청이 하나의 평가 작업과 결과를 공유한다.",
            survival_time_hours=context.rule_based_time_anchor_hours,
        )


async def test_concurrent_result_requests_share_one_evaluation() -> None:
    sessions = SessionRepository(timedelta(hours=2))
    evaluator = CountingEvaluator()
    service = SurvivalService(sessions, load_item_catalog(), load_survival_type_catalog(), evaluator)
    started = await service.start_game(START)
    first, second = await asyncio.gather(
        service.create_result(started.session_id),
        service.create_result(started.session_id),
    )
    assert evaluator.calls == 1
    assert first == second
