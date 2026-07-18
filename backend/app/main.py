import asyncio
import logging
import time
from contextlib import asynccontextmanager, suppress
from datetime import timedelta

from fastapi import FastAPI, Request
from fastapi.encoders import jsonable_encoder
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from google import genai

from app.ai.evaluator import GeminiSurvivalEvaluator, ResilientEvaluator
from app.api.game import router as game_router
from app.core.config import Settings, get_settings
from app.core.errors import DomainError
from app.core.observability import Metrics, request_id_var, valid_request_id
from app.domain.rules import validate_survival_type_rules
from app.repositories.catalogs import load_item_catalog, load_survival_type_catalog
from app.repositories.sessions import SessionRepository
from app.services.survival import SurvivalService

logger = logging.getLogger(__name__)


async def cleanup_sessions(app: FastAPI, interval: int) -> None:
    while True:
        await asyncio.sleep(interval)
        removed = app.state.sessions.cleanup_expired()
        if removed:
            app.state.metrics.increment("bagscape.session.expired.total", removed)
            logger.info("expired sessions removed", extra={"count": removed})


def create_app(settings: Settings | None = None) -> FastAPI:
    settings = settings or get_settings()

    @asynccontextmanager
    async def lifespan(app: FastAPI):
        items = load_item_catalog()
        survival_types = load_survival_type_catalog()
        validate_survival_type_rules(survival_types)
        sessions = SessionRepository(timedelta(seconds=settings.session_ttl_seconds))
        metrics = Metrics()
        primary = None
        client = None
        if settings.gemini_api_key:
            client = genai.Client(api_key=settings.gemini_api_key).aio
            primary = GeminiSurvivalEvaluator(
                client,
                settings.gemini_model,
                settings.gemini_max_concurrency,
                timeout_seconds=settings.gemini_timeout_seconds,
                max_attempts=settings.gemini_max_attempts,
                retry_base_seconds=settings.gemini_retry_base_seconds,
                retry_max_seconds=settings.gemini_retry_max_seconds,
                metrics=metrics,
            )
        evaluator = ResilientEvaluator(
            primary,
            timeout_seconds=max(0.001, settings.result_request_timeout_seconds - 0.05),
            metrics=metrics,
        )
        app.state.items = items
        app.state.survival_types = survival_types
        app.state.sessions = sessions
        app.state.metrics = metrics
        app.state.service = SurvivalService(
            sessions,
            items,
            survival_types,
            evaluator,
            result_timeout_seconds=settings.result_request_timeout_seconds,
            metrics=metrics,
        )
        app.state.cleanup_task = asyncio.create_task(cleanup_sessions(app, settings.session_cleanup_seconds))
        try:
            yield
        finally:
            app.state.cleanup_task.cancel()
            with suppress(asyncio.CancelledError):
                await app.state.cleanup_task
            await sessions.close()
            if client is not None:
                await client.aclose()

    application = FastAPI(title=settings.app_name, version="3.0.0", lifespan=lifespan)
    application.include_router(game_router)

    @application.middleware("http")
    async def observe_request(request: Request, call_next):
        request_id = valid_request_id(request.headers.get("X-Request-ID"))
        token = request_id_var.set(request_id)
        started = time.perf_counter()
        try:
            response = await call_next(request)
            response.headers["X-Request-ID"] = request_id
            return response
        finally:
            duration = time.perf_counter() - started
            metrics = getattr(request.app.state, "metrics", None)
            if metrics is not None:
                metrics.observe("http.server.request.duration", duration)
            route = request.scope.get("route")
            logger.info(
                "HTTP request completed",
                extra={
                    "request_id": request_id,
                    "method": request.method,
                    "route_template": getattr(route, "path", request.url.path),
                    "status_code": getattr(locals().get("response"), "status_code", 500),
                    "duration_ms": round(duration * 1000, 3),
                },
            )
            request_id_var.reset(token)

    @application.exception_handler(DomainError)
    async def handle_domain_error(_: Request, exc: DomainError) -> JSONResponse:
        return JSONResponse(status_code=exc.status_code, content={"error": {"code": exc.code, "message": exc.message}})

    @application.exception_handler(RequestValidationError)
    async def handle_validation_error(_: Request, exc: RequestValidationError) -> JSONResponse:
        return JSONResponse(
            status_code=422,
            content={
                "error": {
                    "code": "VALIDATION_ERROR",
                    "message": "Request validation failed",
                    "details": jsonable_encoder(exc.errors()),
                }
            },
        )

    @application.exception_handler(Exception)
    async def handle_unexpected_error(_: Request, exc: Exception) -> JSONResponse:
        logger.exception("Unhandled server error", extra={"request_id": request_id_var.get()})
        return JSONResponse(
            status_code=500,
            content={"error": {"code": "INTERNAL_SERVER_ERROR", "message": "Internal server error"}},
        )

    @application.get("/health/live", tags=["health"])
    async def live() -> dict[str, str]:
        return {"status": "ok"}

    @application.get("/health/ready", tags=["health"])
    async def ready(request: Request) -> JSONResponse:
        is_ready = (
            bool(request.app.state.items)
            and bool(request.app.state.survival_types)
            and hasattr(request.app.state, "service")
        )
        return JSONResponse(
            status_code=200 if is_ready else 503, content={"status": "ready" if is_ready else "not_ready"}
        )

    return application


app = create_app()
