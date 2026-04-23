from datetime import datetime, timedelta, timezone

from jose import JWTError, jwt

from app.core.config import get_settings
from app.models.enums import AuthProvider, UserRole


def create_access_token(
    user_id:  str,
    provider: AuthProvider,
    role:     UserRole,
    username: str | None = None,
) -> str:
    """
    .NET JwtTokenService와 동일한 알고리즘(HS256) / Issuer / Audience로 JWT 발급.
    role 클레임 포함 → Admin 접근 제어에 사용.
    """
    s = get_settings()
    expire = datetime.now(timezone.utc) + timedelta(hours=s.jwt_expiry_hours)

    payload = {
        "sub":      user_id,
        "provider": provider.value,
        "role":     role.value,
        "username": username,
        "iss":      s.jwt_issuer,
        "aud":      s.jwt_audience,
        "iat":      datetime.now(timezone.utc),
        "exp":      expire,
    }
    return jwt.encode(payload, s.jwt_secret, algorithm=s.jwt_algorithm)


def verify_token(token: str) -> dict:
    """토큰 검증 — 실패 시 JWTError 발생"""
    s = get_settings()
    return jwt.decode(
        token,
        s.jwt_secret,
        algorithms=[s.jwt_algorithm],
        audience=s.jwt_audience,
        issuer=s.jwt_issuer,
    )
