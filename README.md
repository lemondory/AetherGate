# AetherGate

Akka.NET 기반 MMO 게임 서버 포트폴리오 프로젝트.

Actor 모델의 핵심 개념(Supervision, FSM, PipeTo 패턴)을 실제 게임 서버에 적용하고,
FastAPI 웹 레이어와 통합해 인증 · 운영 관리 기능을 구현했습니다.

---

## 기술 스택

| 분류 | 기술 |
|------|------|
| 게임 서버 | C# / .NET 9, Akka.NET 1.5, Akka.Remote |
| 직렬화 | MessagePack |
| 인증 | JWT HS256 (System.IdentityModel.Tokens.Jwt) |
| 웹 레이어 | Python 3.12, FastAPI, SQLAlchemy async |
| 데이터베이스 | PostgreSQL 16 |
| 캐시 / 브로커 | Redis 7 |
| OAuth | Google, Kakao |
| 컨테이너 | Docker Compose |
| 테스트 | xUnit, Akka.TestKit (80개, 전체 통과) |

---

## 프로젝트 구조

```
AetherGate/
├── server/                        ← C# 게임 서버
│   ├── AetherGate.sln
│   ├── AetherGate.Domain          # 엔티티, 메시지, 패킷 정의 (Akka 참조 없음)
│   ├── AetherGate.Application     # Actor 계층 전체
│   ├── AetherGate.Infrastructure  # JwtTokenService
│   ├── AetherGate.Server          # 진입점 (Program.cs)
│   └── tests/                     # 80개 테스트
├── web/                           ← FastAPI 웹 레이어
│   ├── app/
│   ├── api/             # 인증·운영 API 엔드포인트
│   ├── core/            # 설정, JWT, DB 연결
│   ├── models/          # SQLAlchemy 모델
│   ├── services/        # 비즈니스 로직, Redis 브릿지
│   └── templates/       # Admin 대시보드 (Jinja2)
├── docs/                          # 문서
└── docker-compose.yml             # 전체 스택 통합 실행
```

---

## 포트폴리오 핵심 포인트

**Akka.NET Actor 모델 활용**
- `OneForOneStrategy` Supervision — MonsterActor 장애가 다른 Actor에 영향 없음
- `Become()` FSM — 몬스터 AI 5상태 전환 (Patrol → Detect → Chase → Attack → Return)
- `PipeTo` 패턴 — Actor 내부에서 async/await 대신 비동기 결과를 메시지로 수신
- `IWithTimers` — TickSchedulerActor 메모리 누수 없는 주기적 Tick

**TCP 게임 서버**
- 8바이트 고정 헤더 + MessagePack 페이로드 커스텀 프로토콜
- TCP fragmentation 대응 (`ReadExactAsync` 루프)
- 동적 Actor 생성/소멸 — 던전 인스턴스 라이프사이클

**Python FastAPI 연동**
- .NET ↔ Python JWT Secret 공유 (동일 토큰으로 양쪽 검증)
- Redis Pub/Sub 경유 Admin 명령 전달 (Akka.Remote 직접 접근 없음)
- Guest / Google / Kakao OAuth 통합 인증

---

## 빠른 시작

```bash
# .env 파일 생성
cp .env.example .env   # JWT_SECRET 등 값 입력 필요

# 전체 스택 실행
docker compose up --build

# 접속
# 게임 서버 TCP : localhost:9000
# 관리 대시보드 : http://localhost:8000
```

자세한 내용은 [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md)를 참고하세요.

---

## 문서

- [아키텍처](docs/ARCHITECTURE.md) — Actor 계층, 패킷 프로토콜, 전체 흐름
- [실행 방법](docs/GETTING_STARTED.md) — 로컬 개발 환경 설정
- [테스트](docs/TEST_GUIDE.md) — 서버 구동 순서 및 테스트 방법
