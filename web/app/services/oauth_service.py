"""
Google / Kakao OAuth2 흐름 처리.

공통 구조:
  1. get_*_redirect_url()  → 인증 서버로 보낼 URL 반환
  2. handle_*_callback()   → code 교환 → 사용자 정보 조회 → upsert → JWT 반환
"""

import httpx
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import get_settings
from app.models.enums import AuthProvider
from app.schemas.auth import TokenResponse
from app.services.auth_service import upsert_oauth_user

# ─── Google ──────────────────────────────────────────────────────────────────

GOOGLE_AUTH_URL  = "https://accounts.google.com/o/oauth2/v2/auth"
GOOGLE_TOKEN_URL = "https://oauth2.googleapis.com/token"
GOOGLE_USER_URL  = "https://www.googleapis.com/oauth2/v3/userinfo"


def get_google_redirect_url() -> str:
    cfg = get_settings()
    redirect_uri = f"{cfg.base_url}/auth/google/callback"
    params = "&".join([
        f"client_id={cfg.google_client_id}",
        "response_type=code",
        f"redirect_uri={redirect_uri}",
        "scope=openid%20email%20profile",
        "access_type=offline",
    ])
    return f"{GOOGLE_AUTH_URL}?{params}"


async def handle_google_callback(code: str, db: AsyncSession) -> TokenResponse:
    cfg = get_settings()
    redirect_uri = f"{cfg.base_url}/auth/google/callback"

    async with httpx.AsyncClient() as client:
        # 1. code → access_token 교환
        token_resp = await client.post(
            GOOGLE_TOKEN_URL,
            data={
                "code":          code,
                "client_id":     cfg.google_client_id,
                "client_secret": cfg.google_client_secret,
                "redirect_uri":  redirect_uri,
                "grant_type":    "authorization_code",
            },
        )
        token_resp.raise_for_status()
        access_token = token_resp.json()["access_token"]

        # 2. 사용자 정보 조회
        user_resp = await client.get(
            GOOGLE_USER_URL,
            headers={"Authorization": f"Bearer {access_token}"},
        )
        user_resp.raise_for_status()
        info = user_resp.json()

    provider_id = info["sub"]                          # Google 고유 사용자 ID
    username    = info.get("name") or info.get("email")

    return await upsert_oauth_user(db, AuthProvider.GOOGLE, provider_id, username)


# ─── Kakao ───────────────────────────────────────────────────────────────────

KAKAO_AUTH_URL  = "https://kauth.kakao.com/oauth/authorize"
KAKAO_TOKEN_URL = "https://kauth.kakao.com/oauth/token"
KAKAO_USER_URL  = "https://kapi.kakao.com/v2/user/me"


def get_kakao_redirect_url() -> str:
    cfg = get_settings()
    redirect_uri = f"{cfg.base_url}/auth/kakao/callback"
    params = "&".join([
        f"client_id={cfg.kakao_client_id}",
        "response_type=code",
        f"redirect_uri={redirect_uri}",
    ])
    return f"{KAKAO_AUTH_URL}?{params}"


async def handle_kakao_callback(code: str, db: AsyncSession) -> TokenResponse:
    cfg = get_settings()
    redirect_uri = f"{cfg.base_url}/auth/kakao/callback"

    async with httpx.AsyncClient() as client:
        # 1. code → access_token 교환
        token_resp = await client.post(
            KAKAO_TOKEN_URL,
            data={
                "code":          code,
                "client_id":     cfg.kakao_client_id,
                "client_secret": cfg.kakao_client_secret,
                "redirect_uri":  redirect_uri,
                "grant_type":    "authorization_code",
            },
            headers={"Content-Type": "application/x-www-form-urlencoded"},
        )
        token_resp.raise_for_status()
        access_token = token_resp.json()["access_token"]

        # 2. 사용자 정보 조회
        user_resp = await client.get(
            KAKAO_USER_URL,
            headers={"Authorization": f"Bearer {access_token}"},
        )
        user_resp.raise_for_status()
        info = user_resp.json()

    provider_id = str(info["id"])                      # Kakao 고유 사용자 ID
    kakao_account = info.get("kakao_account", {})
    profile   = kakao_account.get("profile", {})
    username  = profile.get("nickname") or kakao_account.get("email")

    return await upsert_oauth_user(db, AuthProvider.KAKAO, provider_id, username)
