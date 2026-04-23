from fastapi import APIRouter, Depends, Request, status
from fastapi.responses import HTMLResponse, RedirectResponse
from fastapi.templating import Jinja2Templates
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.api.deps import require_admin_role
from app.core.database import get_db
from app.models.user import User
from app.services import game_bridge

router = APIRouter(prefix="/admin", tags=["admin"])
templates = Jinja2Templates(directory="app/templates")


# ─── 페이지 라우트 ────────────────────────────────────────────────────────────

@router.get("/login", response_class=HTMLResponse, include_in_schema=False)
async def login_page(request: Request) -> HTMLResponse:
    return templates.TemplateResponse(request, "admin/login.html")


@router.get("/dashboard", response_class=HTMLResponse, include_in_schema=False)
async def dashboard_page(request: Request) -> HTMLResponse:
    return templates.TemplateResponse(request, "admin/dashboard.html")


# ─── API ─────────────────────────────────────────────────────────────────────

@router.get("/stats", summary="대시보드 통계")
async def stats(
    payload: dict = Depends(require_admin_role),
    db: AsyncSession = Depends(get_db),
) -> dict:
    """
    대시보드용 통계 데이터.
    현재 접속자는 Redis 기반 세션 정보가 없으므로 DB 기준 최근 가입자로 대체.
    """
    total_users: int = await db.scalar(select(func.count()).select_from(User)) or 0

    # 최근 가입한 유저 10명 (접속자 목록 시뮬레이션 — 실 서버 연동 전 placeholder)
    result = await db.execute(
        select(User).order_by(User.created_at.desc()).limit(10)
    )
    recent = result.scalars().all()

    players = [
        {
            "id":           str(u.id),
            "username":     u.username,
            "provider":     u.provider.value,
            "connected_at": u.created_at.strftime("%Y-%m-%d %H:%M") if u.created_at else None,
        }
        for u in recent
    ]

    return {
        "server_online":  True,
        "online_players": len(players),
        "total_users":    total_users,
        "my_role":        payload.get("role", "admin"),
        "players":        players,
    }


@router.post("/kick/{player_id}", status_code=status.HTTP_204_NO_CONTENT)
async def kick_player(
    player_id: str,
    _: dict = Depends(require_admin_role),
) -> None:
    """
    특정 플레이어 강제 퇴장.
    Redis PUBLISH → .NET AdminBridgeActor → WorldActor.KickPlayerRequest
    """
    await game_bridge.kick_player(player_id)


@router.post("/broadcast", status_code=status.HTTP_204_NO_CONTENT)
async def broadcast(
    message: str,
    _: dict = Depends(require_admin_role),
) -> None:
    """
    전체 공지 전송.
    Redis PUBLISH → .NET AdminBridgeActor → 모든 ZoneActor → [SYSTEM] 채팅
    """
    await game_bridge.broadcast_message(message)
