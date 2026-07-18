from functools import lru_cache
from secrets import token_hex

from pydantic import Field, model_validator
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")
    app_name: str = "BAGSCAPE API"
    gemini_api_key: str | None = None
    gemini_model: str = "gemini-3.5-flash"
    gemini_timeout_seconds: float = Field(default=8.0, gt=0, le=120)
    result_request_timeout_seconds: float = Field(default=10.0, gt=0, le=180)
    gemini_max_attempts: int = Field(default=2, ge=1, le=5)
    gemini_retry_base_seconds: float = Field(default=0.25, ge=0, le=10)
    gemini_retry_max_seconds: float = Field(default=1.0, ge=0, le=30)
    gemini_max_concurrency: int = Field(default=8, ge=1, le=256)
    session_ttl_seconds: int = Field(default=7_200, gt=0)
    session_cleanup_seconds: int = Field(default=300, gt=0)
    log_hmac_salt: str = Field(default_factory=lambda: token_hex(32), min_length=16)

    @model_validator(mode="after")
    def validate_retry_window(self) -> "Settings":
        if self.gemini_retry_base_seconds > self.gemini_retry_max_seconds:
            raise ValueError("gemini_retry_base_seconds cannot exceed gemini_retry_max_seconds")
        return self


@lru_cache
def get_settings() -> Settings:
    return Settings()
