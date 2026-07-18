from datetime import timedelta

from app.ai.evaluator import fallback_response
from app.ai.prompts import (
    PROMPT_VERSION,
    build_user_prompt,
    load_few_shots,
    load_research_facts,
    render_system_prompt,
)
from app.domain.preprocessor import build_evaluation_context, snapshot_session
from app.repositories.catalogs import load_item_catalog, load_survival_type_catalog
from app.repositories.sessions import SessionRepository
from app.schemas.game import GameStartRequest


def test_prompt_assets_are_valid_and_traceable() -> None:
    facts = load_research_facts()
    examples = load_few_shots()
    assert PROMPT_VERSION == "3.0.0"
    assert len(facts) >= 2
    assert len(examples) >= 3
    assert all(fact.source_organization and fact.reviewed_by for fact in facts)


def test_rendered_prompt_contains_all_assets() -> None:
    prompt = render_system_prompt()
    for fact in load_research_facts():
        assert fact.fact_id in prompt
    for example in load_few_shots():
        assert example.situation_ko in prompt
    repository = SessionRepository(timedelta(hours=1))
    session = repository.create(GameStartRequest(gender="male", age_group="teen", disaster="fire"))
    context = build_evaluation_context(snapshot_session(session), load_item_catalog(), load_survival_type_catalog())
    user_prompt = build_user_prompt(context)
    assert user_prompt.count("<context>") == 1
    assert user_prompt.count("</context>") == 1


def test_fallback_narrative_covers_every_required_evaluation_dimension() -> None:
    repository = SessionRepository(timedelta(hours=1))
    session = repository.create(GameStartRequest(gender="male", age_group="teen", disaster="fire"))
    context = build_evaluation_context(snapshot_session(session), load_item_catalog(), load_survival_type_catalog())
    narrative = fallback_response(context).evaluation_narrative
    for phrase in ["필요 수분", "중요도", "재난 관련", "전체 가방", "행동 기록", "생존 시간"]:
        assert phrase in narrative
