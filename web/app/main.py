from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.responses import RedirectResponse

from app.api import auth, admin
from app.core.config import get_settings
from app.core.database import Base, engine


@asynccontextmanager
async def lifespan(app: FastAPI):
    # 시작 시 테이블 자동 생성 (개발용 — 프로덕션은 Alembic migration 사용)
    async with engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield


def create_app() -> FastAPI:
    app = FastAPI(
        title="AetherGate Web",
        description="AetherGate MMO 서버 — 인증 및 운영 관리 API",
        version="0.1.0",
        lifespan=lifespan,
    )
    app.include_router(auth.router)
    app.include_router(admin.router)

    @app.get("/", include_in_schema=False)
    async def root() -> RedirectResponse:
        return RedirectResponse(url="/admin/login")

    return app


app = create_app()
