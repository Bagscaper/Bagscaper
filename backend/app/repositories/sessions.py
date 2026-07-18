import asyncio
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from enum import StrEnum
from typing import Any
from uuid import UUID, uuid4

from app.core.errors import session_expired, session_not_found
from app.schemas.game import GameLogRequest, GameLogResponse, GameStartRequest, SurvivalResponse


def utcnow() -> datetime:
    return datetime.now(timezone.utc)


class SessionStatus(StrEnum):
    ACTIVE = "ACTIVE"
    RESULT_PENDING = "RESULT_PENDING"
    COMPLETED = "COMPLETED"


@dataclass(frozen=True)
class ActionLogEntry:
    request: GameLogRequest
    received_at: datetime
    applied: bool


@dataclass
class GameSession:
    session_id: UUID
    start_request: GameStartRequest
    created_at: datetime
    last_activity_at: datetime
    expires_at: datetime
    inventory: dict[UUID, str] = field(default_factory=dict)
    action_logs: list[ActionLogEntry] = field(default_factory=list)
    action_requests: dict[UUID, GameLogRequest] = field(default_factory=dict)
    action_responses: dict[UUID, GameLogResponse] = field(default_factory=dict)
    status: SessionStatus = SessionStatus.ACTIVE
    cached_result: SurvivalResponse | None = None
    result_task: asyncio.Task[SurvivalResponse] | None = None
    result_snapshot: Any | None = None
    lock: asyncio.Lock = field(default_factory=asyncio.Lock)


class SessionRepository:
    def __init__(self, ttl: timedelta) -> None:
        self.ttl = ttl
        self._sessions: dict[UUID, GameSession] = {}
        self._expired_ids: set[UUID] = set()
        self._action_registry: dict[UUID, GameLogRequest] = {}
        self._action_registry_lock = asyncio.Lock()

    def create(self, request: GameStartRequest) -> GameSession:
        now = utcnow()
        session = GameSession(uuid4(), request, now, now, now + self.ttl)
        self._sessions[session.session_id] = session
        return session

    def get(self, session_id: UUID) -> GameSession:
        session = self._sessions.get(session_id)
        if session is None:
            if session_id in self._expired_ids:
                raise session_expired()
            raise session_not_found()
        if session.status != SessionStatus.RESULT_PENDING and session.expires_at <= utcnow():
            self._sessions.pop(session_id, None)
            self._expired_ids.add(session_id)
            raise session_expired()
        return session

    def touch(self, session: GameSession) -> None:
        now = utcnow()
        session.last_activity_at = now
        session.expires_at = now + self.ttl

    async def claim_action_id(self, request: GameLogRequest) -> None:
        """Reserve a client idempotency key across every live session."""
        async with self._action_registry_lock:
            previous = self._action_registry.get(request.action_id)
            if previous is not None and previous != request:
                from app.core.errors import action_id_conflict

                raise action_id_conflict()
            self._action_registry[request.action_id] = request

    async def close(self) -> None:
        tasks = [
            session.result_task
            for session in self._sessions.values()
            if session.result_task is not None and not session.result_task.done()
        ]
        for task in tasks:
            task.cancel()
        if tasks:
            await asyncio.gather(*tasks, return_exceptions=True)

    def cleanup_expired(self) -> int:
        now = utcnow()
        expired = [
            sid
            for sid, session in self._sessions.items()
            if session.status != SessionStatus.RESULT_PENDING and session.expires_at <= now
        ]
        for sid in expired:
            self._sessions.pop(sid, None)
            self._expired_ids.add(sid)
        if expired:
            expired_set = set(expired)
            self._action_registry = {
                action_id: request
                for action_id, request in self._action_registry.items()
                if request.session_id not in expired_set
            }
        return len(expired)
