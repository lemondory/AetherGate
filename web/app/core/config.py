from functools import lru_cache
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    # JWT — .NET JwtTokenService와 공유
    jwt_secret:      str
    jwt_issuer:      str = "AetherGate"
    jwt_audience:    str = "AetherGateClient"
    jwt_algorithm:   str = "HS256"
    jwt_expiry_hours: int = 24

    # Database (PostgreSQL)
    database_url: str = "postgresql+asyncpg://postgres:postgres@localhost:5432/aethergate"

    # Redis
    redis_url: str = "redis://localhost:6379"

    # 서버 베이스 URL — OAuth 콜백 URI 생성에 사용
    base_url: str = "http://localhost:8000"

    # Guest 모드 활성화 여부
    guest_enabled: bool = True

    # OAuth — 앱 등록 후 설정
    google_client_id:     str = ""
    google_client_secret: str = ""
    kakao_client_id:      str = ""
    kakao_client_secret:  str = ""

    model_config = SettingsConfigDict(env_file=".env", env_file_encoding="utf-8")


@lru_cache
def get_settings() -> Settings:
    return Settings()  # type: ignore[call-arg]
