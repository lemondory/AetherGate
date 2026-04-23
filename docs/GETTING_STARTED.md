# AetherGate — 실행 방법

## 사전 준비

- Docker Desktop
- (로컬 개발용) .NET 9 SDK, Python 3.12

---

## Docker Compose로 전체 실행

```bash
# 1. 저장소 클론
git clone <this-repo>
cd AetherGate

# 2. 환경변수 설정
cp .env.example .env
# .env 파일 열어 JWT_SECRET 등 입력
```

**.env 필수 항목**

```env
JWT_SECRET=your-strong-secret-here
```

**선택 항목** (OAuth 사용 시)

```env
GOOGLE_CLIENT_ID=...
GOOGLE_CLIENT_SECRET=...
KAKAO_CLIENT_ID=...
KAKAO_CLIENT_SECRET=...
```

```bash
# 3. 전체 스택 실행
docker compose up --build

# 4. 접속 확인
# 게임 서버 TCP   : localhost:9000
# 관리 대시보드   : http://localhost:8000
# FastAPI Swagger : http://localhost:8000/docs
```

---

## 로컬 개발 환경 (개별 실행)

### .NET 게임 서버

```bash
# DB / Redis 먼저 실행
docker compose up postgres redis -d

# appsettings.json 또는 환경변수로 설정 주입
export JWT_SECRET=dev-secret
export REDIS_URL=localhost:6379

dotnet run --project server/AetherGate.Server
```

### FastAPI 웹 서버

```bash
cd web

python3 -m venv .venv
source .venv/bin/activate      # Windows: .venv\Scripts\activate

pip install -r requirements.txt

cp .env.example .env
# .env 에 JWT_SECRET 입력 (.NET 서버와 동일한 값)

uvicorn app.main:app --reload --port 8000
```

브라우저에서 `http://localhost:8000` 접속 → 관리자 로그인 페이지로 이동.

---

## 환경변수 전체 목록

| 변수 | 기본값 | 설명 |
|------|--------|------|
| `JWT_SECRET` | — | **필수.** .NET ↔ Python 공유 시크릿 |
| `JWT_ISSUER` | AetherGate | JWT iss 클레임 |
| `JWT_AUDIENCE` | AetherGateClient | JWT aud 클레임 |
| `POSTGRES_USER` | postgres | DB 계정 |
| `POSTGRES_PASSWORD` | postgres | DB 비밀번호 |
| `REDIS_URL` | redis:6379 (.NET) / redis://redis:6379 (Python) | Redis 연결 |
| `BASE_URL` | http://localhost:8000 | OAuth 콜백 URI 기준 URL |
| `GUEST_ENABLED` | true | Guest 인증 on/off |
| `GOOGLE_CLIENT_ID` | — | Google OAuth 앱 ID |
| `GOOGLE_CLIENT_SECRET` | — | Google OAuth 앱 시크릿 |
| `KAKAO_CLIENT_ID` | — | Kakao OAuth 앱 ID |
| `KAKAO_CLIENT_SECRET` | — | Kakao OAuth 앱 시크릿 |

---

## 포트 정리

| 포트 | 서비스 | 설명 |
|------|--------|------|
| 9000 | .NET 게임 서버 | 클라이언트 TCP 연결 |
| 8090 | .NET 게임 서버 | Akka.Remote (내부 통신) |
| 8000 | FastAPI | HTTP API · 관리 대시보드 |
| 5432 | PostgreSQL | DB |
| 6379 | Redis | 캐시 · Pub/Sub |
