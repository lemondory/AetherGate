# AetherGate — 테스트 가이드

## 자동화 테스트 결과 (.NET 게임 서버)

| 계층 | 범위 | 테스트 수 | 결과 |
|------|------|-----------|------|
| 1 — 단위 | PacketSerializer, PacketFramer, MonsterActor FSM, InventoryItem | 36 | 전체 통과 |
| 1 — 단위 | PlayerActor | 13 | 전체 통과 |
| 1 — 단위 | AuthActor | 8 | 전체 통과 |
| 2 — 통합 | ZoneActor (Akka.TestKit) | 9 | 전체 통과 |
| 3 — E2E | 실제 TCP 소켓 시나리오 | 5 | 전체 통과 |
| **합계** | | **80** | **80 / 80** |

---

## 1단계 — 웹 서버 구동 및 인증 테스트

웹 서버(FastAPI)를 먼저 구동해 인증 흐름을 확인합니다.

### 사전 준비

```bash
# PostgreSQL · Redis 실행
cd AetherGate
docker compose up postgres redis -d

# 웹 서버 실행
cd web
source .venv/bin/activate
uvicorn app.main:app --reload --port 8000
```

### 관리자 대시보드 확인

브라우저에서 `http://localhost:8000` 접속 → `/admin/login` 리디렉트 확인.

### Guest 인증 흐름

```bash
# 1. Guest 계정 발급
curl -s -X POST http://localhost:8000/auth/guest | jq .
# 응답: { "access_token": "...", "guest_key": "..." }

# guest_key를 저장해두고 로그인 테스트
curl -s -X POST http://localhost:8000/auth/guest/login \
  -H "Content-Type: application/json" \
  -d '{"guest_key": "<위에서 받은 guest_key>"}' | jq .
# 응답: { "access_token": "..." }
```

### OAuth 로그인 흐름 (앱 등록 후)

```bash
# Google 로그인 시작 — 브라우저에서 열기
open http://localhost:8000/auth/google

# Kakao 로그인 시작
open http://localhost:8000/auth/kakao
```

콜백 후 `access_token` 응답이 오면 정상.

### Guest 모드 비활성화 확인

```bash
# .env 에서 GUEST_ENABLED=false 로 변경 후 서버 재시작
curl -s -X POST http://localhost:8000/auth/guest
# 응답: 403 Forbidden - "Guest login is disabled."
```

### API 문서 확인

```
http://localhost:8000/docs
```

전체 엔드포인트 목록 및 Swagger UI에서 직접 요청 테스트 가능.

---

## 2단계 — 게임 서버 구동

웹 서버 인증으로 발급받은 JWT를 게임 서버에서 검증하는 흐름 확인.

### 게임 서버 실행

```bash
cd AetherGate

# Docker Compose 전체 실행 (웹 서버 포함)
docker compose up --build

# 또는 로컬에서 게임 서버만 실행
export JWT_SECRET=<.env와 동일한 값>
export REDIS_URL=localhost:6379
dotnet run --project server/AetherGate.Server
```

### 서버 기동 확인

```
포트 9000 — 클라이언트 TCP 연결 대기
포트 8090 — Akka.Remote 내부 통신
```

### JWT 연동 확인

```bash
# 1. 웹 서버에서 토큰 발급
TOKEN=$(curl -s -X POST http://localhost:8000/auth/guest \
  | jq -r .access_token)

# 2. 발급된 토큰을 게임 클라이언트 LoginPacket에 담아 TCP 9000으로 전송
#    게임 서버가 동일한 JWT_SECRET으로 검증 → LoginResult 응답 확인
```

### Admin 명령 연동 확인

```bash
# 관리자 권한 토큰 필요 (DB에서 role을 admin으로 변경 후 재로그인)
ADMIN_TOKEN=<admin 토큰>

# 전체 공지 발송 → Redis → AdminBridgeActor → 게임 내 [SYSTEM] 채팅
curl -s -X POST "http://localhost:8000/admin/broadcast?message=서버점검예정" \
  -H "Authorization: Bearer $ADMIN_TOKEN"

# 플레이어 강제 퇴장
curl -s -X POST http://localhost:8000/admin/kick/<player_id> \
  -H "Authorization: Bearer $ADMIN_TOKEN"
```

---

## 3단계 — .NET 자동화 테스트 실행

```bash
cd AetherGate

# 전체 실행
dotnet test

# 계층별 실행
dotnet test --filter "FullyQualifiedName~Network"       # 패킷 (Serializer + Framer)
dotnet test --filter "FullyQualifiedName~MonsterActor"  # 몬스터 FSM
dotnet test --filter "FullyQualifiedName~PlayerActor"   # 플레이어 로직
dotnet test --filter "FullyQualifiedName~AuthActor"     # 인증
dotnet test --filter "FullyQualifiedName~Inventory"     # 도메인: InventoryItem
dotnet test --filter "FullyQualifiedName~ZoneActor"     # 통합: Zone
dotnet test --filter "FullyQualifiedName~EndToEnd"      # E2E TCP
```

E2E 테스트는 내부적으로 `TcpListener`를 직접 생성하므로 게임 서버 별도 실행 없이 동작합니다.

---

## 테스트 범위 상세

### 단위 테스트

| 파일 | 테스트 수 | 주요 검증 |
|------|-----------|-----------|
| PacketSerializerTests | 8 | 전 패킷 타입 MessagePack 왕복 |
| PacketFramerTests | 5 | 프레임 읽기/쓰기, 단편화, 연결 종료 |
| MonsterActorTests | 11 | FSM 5상태 전환, 피해·사망 |
| PlayerActorTests | 13 | 이동, 스킬·쿨다운, 구매, 강화, 피해·사망 |
| AuthActorTests | 8 | 로그인 성공·실패, 토큰 생성·검증 |
| InventoryItemTests | 15 | 강화 성공률 테이블, TryConsume, Add |

### 통합 테스트 (Akka.TestKit)

| 파일 | 테스트 수 | 주요 검증 |
|------|-----------|-----------|
| ZoneActorTests | 9 | 입장·퇴장 브로드캐스트, 이동·전투·채팅·귓속말·골드 격리 |

### E2E 테스트

| 파일 | 테스트 수 | 주요 검증 |
|------|-----------|-----------|
| EndToEndTests | 5 | 실제 TCP 연결, 다중 클라이언트, 정상 종료, 50KB 단편화 |

---

## 테스트 중 발견·수정된 버그

| # | 컴포넌트 | 내용 | 수정 |
|---|----------|------|------|
| 1 | MonsterActor | 생성자에서 `PreStart` 이전 `Tell()` 호출 — 상태 메시지 순서 문제 | 완료 |
| 2 | ZoneActor | `ScanForPlayers` 핸들러 누락 — 몬스터가 Patrol에서 영구 정지 | 완료 |
| 3 | ZoneActor | `WhisperMessage` 핸들러 누락 — 귓속말 미전달 | 완료 |
| 4 | ZoneActor | `AreaSkillRequest` 핸들러 누락 — 범위 스킬 미처리 | 완료 |
| 5 | ZoneActor | `CombatDamage` HP 미감소 — MonsterActor · PlayerActor에 미전달 | 완료 |
| 6 | SessionActor | `SendFailed` 핸들러 누락 — 연결 끊김 미감지 | 완료 |
| 7 | WorldActor | `Guid.NewGuid():N[..8]` 포맷 오류 → `.ToString("N")[..8]` | 완료 |
| 8 | MonsterActor | AK1004 경고 — `ScheduleTellOnceCancelable` + PostStop 취소로 수정 | 완료 |
