import uuid

from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.security import create_access_token
from app.models.enums import AuthProvider, UserRole
from app.models.user import User
from app.schemas.auth import TokenResponse


async def create_guest(db: AsyncSession) -> TokenResponse:
    """Guest 계정 신규 발급 — UUID를 guest_key로 사용"""
    guest_key = str(uuid.uuid4())
    user = User(
        provider=AuthProvider.GUEST,
        provider_id=guest_key,
        role=UserRole.USER,
    )
    db.add(user)
    await db.commit()
    await db.refresh(user)

    token = create_access_token(str(user.id), user.provider, user.role, user.username)
    return TokenResponse(access_token=token, guest_key=guest_key)


async def login_guest(db: AsyncSession, guest_key: str) -> TokenResponse | None:
    """기존 Guest 계정으로 로그인 — guest_key 분실 시 None 반환"""
    result = await db.execute(
        select(User).where(
            User.provider == AuthProvider.GUEST,
            User.provider_id == guest_key,
        )
    )
    user = result.scalar_one_or_none()
    if user is None:
        return None

    token = create_access_token(str(user.id), user.provider, user.role, user.username)
    return TokenResponse(access_token=token)


async def upsert_oauth_user(
    db:          AsyncSession,
    provider:    AuthProvider,
    provider_id: str,
    username:    str | None = None,
) -> TokenResponse:
    """OAuth 로그인/회원가입 통합 — provider + provider_id 기준 upsert"""
    result = await db.execute(
        select(User).where(
            User.provider == provider,
            User.provider_id == provider_id,
        )
    )
    user = result.scalar_one_or_none()

    if user is None:
        user = User(
            provider=provider,
            provider_id=provider_id,
            username=username,
            role=UserRole.USER,
        )
        db.add(user)
        await db.commit()
        await db.refresh(user)
    elif username and user.username is None:
        user.username = username
        await db.commit()

    token = create_access_token(str(user.id), user.provider, user.role, user.username)
    return TokenResponse(access_token=token)
