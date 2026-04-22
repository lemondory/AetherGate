# AetherGate — 아키텍처

## 전체 구성

```
[게임 클라이언트]
    │
    ├── TCP 9000 ──────────────→ [AetherGate .NET 서버]   (실시간 게임 통신)
    │                                    │
    └── HTTP 8000 ─────────────→ [aethergate-web FastAPI]  (인증 · 운영 관리)
                                         │
                           ┌─────────────┼─────────────┐
                           ▼             ▼             ▼
                       [PostgreSQL]   [Redis]    [Redis Pub/Sub]
                        (회원 DB)    (세션 캐시)  (운영 명령 전달)
                                                       │
                                                       ▼
                                          [AetherGate .NET 서버]
                                          AdminBridgeActor → Actor.Tell
```

---

## Actor 계층

```
GameServerActor          (최상위, OneForOneStrategy Supervision)
├── AuthActor            JWT 발급 · 검증
├── AdminBridgeActor     Redis "admin:commands" 구독 → Actor 명령 변환
├── WorldActor           존 · 던전 인스턴스 관리
│   ├── ZoneActor[field_01]   필드 존
│   │   ├── PlayerActor × N   플레이어 상태 (HP/MP/인벤토리/골드)
│   │   ├── MonsterActor × N  몬스터 FSM AI
│   │   ├── ChatActor         존 채팅 · 귓속말
│   │   └── TickSchedulerActor  100ms 주기 Tick
│   └── ZoneActor[dungeon_*]  던전 인스턴스 (동적 생성/소멸)
└── GatewayActor         TCP 연결 수락
    └── SessionActor × N  연결 1개당 1 Actor (Become FSM)
```

### 핵심 설계 결정

**Supervision Strategy**
MonsterActor 예외 → Restart. 플레이어 세션에 영향 없음.

**Become() FSM**
SessionActor: `Unauthenticated` → `Authenticated` 상태 전환.
MonsterActor: 5상태 AI (아래 참고).

**PipeTo 패턴**
Actor 내부에서 async/await 직접 사용 금지.
`AcceptTcpClientAsync`, `ReadPacketAsync` 결과를 `PipeTo(Self)`로 메시지화.

**WorldActor SessionEnrollment**
ZoneActor가 SessionActor 참조를 직접 갖지 않음.
WorldActor가 `player_id → SessionActor` 맵을 관리 → Clean Architecture 유지.

---

## 몬스터 AI FSM

```
Patrol ──(감지 범위 진입)──→ Detect ──(0.5초 딜레이)──→ Chase
  ↑                                                          │
  └─────────(귀환 완료, HP 전회복)── Return ←───────────────┘
                                        ↑          (타겟 소실 / 스폰 이탈)
                                        │
                                     Attack ←──(공격 범위 진입)── Chase
                                        │
                                        └──(사거리 이탈)──→ Chase
```

| 상태 | 동작 |
|------|------|
| Patrol | 스폰 주변 4방향 순찰. 매 Tick ScanForPlayers 요청 |
| Detect | 타겟 발견. 0.5초 대기 후 Chase (ICancelable 타이머) |
| Chase | 타겟 방향 이동. 공격 범위 → Attack, 너무 멀어지면 Return |
| Attack | 1500ms 쿨다운 공격. CombatDamage → ZoneActor → PlayerActor |
| Return | 스폰 귀환, 매 Tick HP 1% 회복. 피격 시 Chase 재진입 |

모든 상태에서 `HandleDamageInAnyState` 공통 처리. HP 0 → `MonsterDied` + `Context.Stop`.

---

## TCP 패킷 프로토콜

### 헤더 (8바이트, LittleEndian)

```
┌──────────────────────────────────────────┐
│ Length   : 4 bytes  (페이로드 길이)       │
│ PacketId : 2 bytes  (패킷 타입)           │
│ Sequence : 2 bytes  (순서 번호)           │
└──────────────────────────────────────────┘
```

직렬화: MessagePack (`[MessagePackObject]`, `[Key(n)]`)

### 클라이언트 → 서버

| PacketId | 이름 | 설명 |
|----------|------|------|
| 0x0001 | Login | 로그인 (JWT 토큰) |
| 0x0101 | Move | 이동 목적지 (x, y) |
| 0x0102 | UseSkill | 스킬 ID, 타겟 |
| 0x0103 | PickupItem | 드롭 아이템 획득 |
| 0x0104 | UseItem | 아이템 사용 |
| 0x0105 | EnhanceItem | 아이템 강화 |
| 0x0106 | BuyItem | 상점 구매 |
| 0x0201 | ZoneChat | 존 채팅 |
| 0x0202 | Whisper | 귓속말 |
| 0x0301 | EnterDungeon | 던전 입장 |

### 서버 → 클라이언트

| PacketId | 이름 | 설명 |
|----------|------|------|
| 0x8001 | LoginResult | 로그인 결과 · 플레이어 ID |
| 0x8101 | PlayerEntered | 다른 플레이어 입장 |
| 0x8102 | PlayerLeft | 플레이어 퇴장 |
| 0x8103 | PlayerMoved | 플레이어 이동 |
| 0x8104 | MonsterMoved | 몬스터 이동 |
| 0x8105 | MonsterStateChanged | 몬스터 AI 상태 전환 |
| 0x8106 | CombatDamage | 피해량 (크리티컬 포함) |
| 0x8107 | SkillUsed | 스킬 시전 |
| 0x8108 | BuffApplied | 버프 적용 |
| 0x8109 | MonsterDied | 몬스터 사망 |
| 0x810A | PlayerEnterResult | 입장 결과 |
| 0x8201 | ItemDropped | 아이템 드롭 |
| 0x8202 | ItemPickedUp | 아이템 획득 |
| 0x8203 | ItemEnhanced | 강화 결과 |
| 0x8204 | GoldChanged | 골드 변동 |
| 0x8301 | ChatMessage | 채팅 메시지 |
| 0x8401 | DungeonCreated | 던전 인스턴스 생성 |
| 0x8402 | DungeonResult | 던전 클리어 결과 |
| 0x8FFF | Error | 오류 응답 |

---

## FastAPI ↔ .NET 연동

### JWT 공유

두 서버가 동일한 `JWT_SECRET` 환경변수를 사용. FastAPI에서 발급한 토큰을 .NET 서버에서 그대로 검증.

```
FastAPI (python-jose, HS256)
  → 토큰 발급
  → 클라이언트가 TCP LoginPacket에 포함
  → .NET JwtTokenService 검증
```

### Admin 명령 전달 (Redis Pub/Sub)

FastAPI → Akka.Remote TCP 직접 통신 대신 Redis를 중간 브로커로 사용.

```
POST /admin/kick/{id}
  → game_bridge.py: Redis PUBLISH "admin:commands" '{"type":"kick","playerId":"..."}'
  → AdminBridgeActor: Redis SUBSCRIBE → Channel<string> → PipeTo(Self)
  → WorldActor: KickPlayerRequest → ZoneActor → SessionActor 연결 종료
```
