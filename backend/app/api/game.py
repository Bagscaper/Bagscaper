from typing import Annotated

from fastapi import APIRouter, Depends, Request, status

from app.schemas.game import (
    GameLogRequest,
    GameLogResponse,
    GameResultRequest,
    GameStartRequest,
    GameStartResponse,
    SurvivalResponse,
)
from app.services.survival import SurvivalService

router = APIRouter(prefix="/game", tags=["game"])


def get_service(request: Request) -> SurvivalService:
    return request.app.state.service


ServiceDep = Annotated[SurvivalService, Depends(get_service)]


@router.post("/start", response_model=GameStartResponse, status_code=status.HTTP_201_CREATED)
async def start_game(payload: GameStartRequest, service: ServiceDep) -> GameStartResponse:
    return await service.start_game(payload)


@router.post(
    "/log",
    response_model=GameLogResponse,
    responses={
        409: {
            "description": "Idempotency, instance identity, inventory limit, or completed-session conflict",
            "content": {
                "application/json": {
                    "examples": {
                        "item_instance_conflict": {
                            "summary": "The instance is linked to another item type",
                            "value": {
                                "error": {
                                    "code": "ITEM_INSTANCE_CONFLICT",
                                    "message": "item_instance_id is already linked to another item_id",
                                }
                            },
                        }
                    }
                }
            },
        },
        422: {"description": "Invalid request or unknown static item_id"},
    },
)
async def log_action(payload: GameLogRequest, service: ServiceDep) -> GameLogResponse:
    return await service.log_action(payload)


@router.post("/result", response_model=SurvivalResponse)
async def create_result(payload: GameResultRequest, service: ServiceDep) -> SurvivalResponse:
    return await service.create_result(payload.session_id)
