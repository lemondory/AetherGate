from fastapi import APIRouter, Depends, HTTPException, Query, status
from fastapi.responses import RedirectResponse
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import get_settings
from app.core.database import get_db
from app.schemas.auth import GuestLoginRequest, TokenResponse
from app.services import auth_service
from app.services import oauth_service

router = APIRouter(prefix="/auth", tags=["auth"])


def _require_guest_enabled() -> None:
    """Guest 모드가 비활성화된 경우 403 반환."""
    if not get_settings().guest_enabled:
        raise HTTPException(
            status_code=status.HTTP_403_FORBIDDEN,
            detail="Guest login is disabled.",
        )


@router.post("/guest", response_model=TokenResponse, status_code=status.HTTP_201_CREATED)
async def guest_register(db: AsyncSession = Depends(get_db)) -> TokenResponse:
    """
    Guest 계정 신규 발급.
    응답의 guest_key를 클라이언트 로컬에 저장해야 합니다.
    앱 삭제 시 guest_key 소멸 → 계정 접근 불가 (의도된 설계).
    """
    _require_guest_enabled()
    return await auth_service.create_guest(db)


@router.post("/guest/login", response_model=TokenResponse)
async def guest_login(
    body: GuestLoginRequest,
    db:   AsyncSession = Depends(get_db),
) -> TokenResponse:
    """기존 Guest 계정 로그인 — guest_key 제시 → JWT 발급"""
    _require_guest_enabled()
    result = await auth_service.login_guest(db, body.guest_key)
    if result is None:
        raise HTTPException(
            status_code=status.HTTP_404_NOT_FOUND,
            detail="Guest account not found. Key may have been lost.",
        )
    return result


# ─── Google OAuth2 ───────────────────────────────────────────────────────────

@router.get("/google", summary="Google 로그인 시작")
async def google_login() -> RedirectResponse:
    """Google 인증 서버로 리디렉트."""
    cfg = get_settings()
    if not cfg.google_client_id:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Google OAuth is not configured.",
        )
    return RedirectResponse(oauth_service.get_google_redirect_url())


@router.get("/google/callback", response_model=TokenResponse, summary="Google 콜백 처리")
async def google_callback(
    code:  str = Query(..., description="Google이 전달한 인증 코드"),
    db:    AsyncSession = Depends(get_db),
) -> TokenResponse:
    """Google에서 받은 code를 토큰으로 교환 후 JWT 발급."""
    try:
        return await oauth_service.handle_google_callback(code, db)
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail=f"Google OAuth failed: {exc}",
        )


# ─── Kakao OAuth2 ────────────────────────────────────────────────────────────

@router.get("/kakao", summary="Kakao 로그인 시작")
async def kakao_login() -> RedirectResponse:
    """Kakao 인증 서버로 리디렉트."""
    cfg = get_settings()
    if not cfg.kakao_client_id:
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Kakao OAuth is not configured.",
        )
    return RedirectResponse(oauth_service.get_kakao_redirect_url())


@router.get("/kakao/callback", response_model=TokenResponse, summary="Kakao 콜백 처리")
async def kakao_callback(
    code:  str = Query(..., description="Kakao가 전달한 인증 코드"),
    db:    AsyncSession = Depends(get_db),
) -> TokenResponse:
    """Kakao에서 받은 code를 토큰으로 교환 후 JWT 발급."""
    try:
        return await oauth_service.handle_kakao_callback(code, db)
    except Exception as exc:
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail=f"Kakao OAuth failed: {exc}",
        )
