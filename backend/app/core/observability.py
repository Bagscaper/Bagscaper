from __future__ import annotations

import contextvars
import hashlib
import hmac
import logging
import time
from collections import Counter
from dataclasses import dataclass, field
from threading import Lock
from uuid import UUID, uuid4

from opentelemetry import metrics as otel_metrics

request_id_var: contextvars.ContextVar[str] = contextvars.ContextVar("request_id", default="-")


def valid_request_id(value: str | None) -> str:
    if value:
        try:
            return str(UUID(value))
        except (ValueError, TypeError, AttributeError):
            pass
    return str(uuid4())


def hash_session_id(session_id: UUID, salt: str) -> str:
    return hmac.new(salt.encode(), str(session_id).encode(), hashlib.sha256).hexdigest()[:16]


class RequestIdFilter(logging.Filter):
    def filter(self, record: logging.LogRecord) -> bool:
        if not hasattr(record, "request_id"):
            record.request_id = request_id_var.get()
        return True


@dataclass
class Metrics:
    """OpenTelemetry facade with an in-memory snapshot for deterministic tests."""

    counters: Counter[str] = field(default_factory=Counter)
    durations: dict[str, list[float]] = field(default_factory=dict)
    _lock: Lock = field(default_factory=Lock)
    _meter: object = field(init=False, repr=False)
    _otel_counters: dict[str, object] = field(default_factory=dict, init=False, repr=False)
    _otel_histograms: dict[str, object] = field(default_factory=dict, init=False, repr=False)

    def __post_init__(self) -> None:
        self._meter = otel_metrics.get_meter("bagscape.backend", "3.0.0")

    def increment(self, name: str, amount: int = 1) -> None:
        with self._lock:
            self.counters[name] += amount
            instrument = self._otel_counters.get(name)
            if instrument is None:
                instrument = self._meter.create_counter(name)  # type: ignore[attr-defined]
                self._otel_counters[name] = instrument
            instrument.add(amount)  # type: ignore[attr-defined]

    def observe(self, name: str, seconds: float) -> None:
        with self._lock:
            self.durations.setdefault(name, []).append(seconds)
            instrument = self._otel_histograms.get(name)
            if instrument is None:
                instrument = self._meter.create_histogram(name, unit="s")  # type: ignore[attr-defined]
                self._otel_histograms[name] = instrument
            instrument.record(seconds)  # type: ignore[attr-defined]

    def snapshot(self) -> dict[str, object]:
        with self._lock:
            return {
                "counters": dict(self.counters),
                "durations": {name: tuple(values) for name, values in self.durations.items()},
            }


class Timer:
    def __init__(self, metrics: Metrics, name: str) -> None:
        self.metrics = metrics
        self.name = name
        self.started = time.perf_counter()

    def stop(self) -> float:
        elapsed = time.perf_counter() - self.started
        self.metrics.observe(self.name, elapsed)
        return elapsed
