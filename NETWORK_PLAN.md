# AetherGate — 네트워크 레이어 구현 계획

## 현재 상태 (완료)

```
[Domain]         ValueObject, Entity, Message(Command/Event) 정의
[Application]    Actor 계층 구조 (GameServer→World→Zone→Player/Monster/Chat/Tick)
[Infrastructure] JwtTokenService
[Server]         Program.cs (ActorSystem 시작점)
```

Actor 간 메시지 흐름은 완성됐으나,  
**실제 클라이언트 ↔ 서버 TCP 통신**은 GatewayActor/SessionActor에 스텁만 존재하는 상태.

---

## 다음 작업: 네트워크 레이어

### 전체 데이터 흐름

```
[Client]
   │  TCP (raw bytes)
   ▼
[GatewayActor]          ← TcpListener로 연결 수락
   │  ClientConnected
   ▼
[SessionActor]          ← 연결 1개 = Actor 1개
   │  패킷 역직렬화
   │  Command 메시지 변환
   ▼
[ZoneActor / AuthActor] ← 게임 로직 처리
   │  Event 메시지
   ▼
[SessionActor]          ← 직렬화 후 클라이언트로 전송
   │  TCP (raw bytes)
   ▼
[Client]
```

---

## 작업 목록

### STEP 1 — 패킷 프로토콜 정의

**위치:** `src/AetherGate.Domain/Network/`

```
Packet 구조 (고정 헤더 + 가변 페이로드)
┌──────────────────────────────────────┐
│ [Length : 4 bytes] 페이로드 길이      │
│ [PacketId : 2 bytes] 패킷 타입        │
│ [Sequence : 2 bytes] 순서 번호        │
│ [Payload : N bytes] MessagePack 직렬화│
└──────────────────────────────────────┘
```

- `PacketId` 열거형 정의 (Login, Move, Attack, Chat 등)
- `IPacket` 인터페이스 정의
- 각 패킷 DTO 정의 (서버↔클라이언트 각각)

### STEP 2 — 직렬화 레이어

**위치:** `src/AetherGate.Infrastructure/Network/`  
**라이브러리:** MessagePack-CSharp

```
PacketSerializer
  Serialize<T>(T packet) → byte[]
  Deserialize<T>(byte[] data) → T
  
PacketFramer (헤더 파싱/조립)
  Frame(packetId, payload) → byte[]  // 전송용 프레임 생성
  TryParse(buffer) → (PacketId, byte[])  // 수신 버퍼 파싱
```

- 패킷 경계 처리 (TCP는 스트림 — 분할/합쳐짐 대응)
- 수신 버퍼 관리

### STEP 3 — GatewayActor TCP 구현

**위치:** `src/AetherGate.Application/Actors/GatewayActor.cs`  
**방식:** `System.Net.Sockets` + `async/await` → Akka `PipeTo`

```csharp
// 연결 수락 루프 → PipeTo(Self) 패턴
private async Task AcceptLoop()
{
    while (!_cts.IsCancellationRequested)
    {
        var client = await _listener.AcceptTcpClientAsync();
        Self.Tell(new RawClientConnected(client));
    }
}

// RawClientConnected 수신 → SessionActor 생성
```

- `TcpListener` 비동기 수락
- `PipeTo` 패턴으로 Actor 스레드 안전성 유지
- Graceful shutdown 처리

### STEP 4 — SessionActor 네트워크 구현

**위치:** `src/AetherGate.Application/Actors/SessionActor.cs`

```
수신 루프 (별도 Task → PipeTo)
  TcpClient.GetStream() → 헤더 읽기 → 페이로드 읽기 → PacketReceived(id, data)

수신 처리
  PacketReceived → 역직렬화 → Command 메시지 → 해당 Actor로 Tell

송신 처리
  Event 수신 → 직렬화 → NetworkStream.WriteAsync
```

- 수신: `async Task ReceiveLoop()` + `PipeTo(Self)`
- 송신: 직렬화된 패킷을 NetworkStream에 비동기 Write
- 연결 끊김 감지 → `GatewayActor.Tell(ClientDisconnected)`

### STEP 5 — PacketRouter (패킷 ID → Command 변환)

**위치:** `src/AetherGate.Application/Network/`

```csharp
// PacketId에 따라 역직렬화 후 적절한 Actor로 라우팅
public static class PacketRouter
{
    public static void Route(PacketId id, byte[] payload,
        string playerId, IActorRef zoneActor, IActorRef authActor)
    {
        switch (id)
        {
            case PacketId.Login:   → authActor.Tell(...)
            case PacketId.Move:    → zoneActor.Tell(...)
            case PacketId.Skill:   → zoneActor.Tell(...)
            case PacketId.Chat:    → zoneActor.Tell(...)
            ...
        }
    }
}
```

---

## 패킷 ID 목록 (초안)

### Client → Server

| PacketId | 이름 | 내용 |
|----------|------|------|
| 0x0001 | Login | username, passwordHash |
| 0x0101 | Move | destination(x, y) |
| 0x0102 | UseSkill | skillId, targetId, targetPos |
| 0x0103 | PickupItem | dropId |
| 0x0104 | UseItem | itemId |
| 0x0105 | EnhanceItem | itemId |
| 0x0106 | BuyItem | itemId, quantity |
| 0x0201 | ZoneChat | message |
| 0x0202 | Whisper | targetName, message |
| 0x0301 | EnterDungeon | dungeonTemplateId |

### Server → Client

| PacketId | 이름 | 내용 |
|----------|------|------|
| 0x8001 | LoginResult | success, token, playerId |
| 0x8101 | PlayerMoved | playerId, x, y |
| 0x8102 | MonsterMoved | monsterId, x, y |
| 0x8103 | MonsterStateChanged | monsterId, state |
| 0x8104 | CombatDamage | attackerId, targetId, damage, isCrit |
| 0x8105 | SkillUsed | casterId, skillId, targetId |
| 0x8106 | MonsterDied | monsterId, killerPlayerId |
| 0x8201 | ItemDropped | dropId, itemId, x, y |
| 0x8202 | ItemPickedUp | playerId, dropId |
| 0x8203 | GoldChanged | delta, newTotal |
| 0x8301 | ChatMessage | senderName, message, isWhisper |
| 0x8401 | DungeonResult | instanceId, isCleared |

---

## 라이브러리 추가 계획

```bash
# 직렬화
dotnet add src/AetherGate.Infrastructure package MessagePack

# (선택) 테스트용 에코 클라이언트
dotnet add tests/AetherGate.Tests package Microsoft.NET.Test.Sdk
```

---

## 작업 순서 (우선순위)

```
1. [Domain]         패킷 프로토콜 정의 (PacketId enum, IPacket, DTO)
2. [Infrastructure] MessagePack 직렬화 + PacketFramer
3. [Application]    GatewayActor TCP 실구현 (AcceptLoop + PipeTo)
4. [Application]    SessionActor 수신/송신 루프 실구현
5. [Application]    PacketRouter (PacketId → Command 변환)
6. [Tests]          SessionActor 통합 테스트 (실제 TCP 연결)
```

---

## 유의 사항

- Actor 내부에서 직접 `await` 금지 → 반드시 `PipeTo(Self)` 패턴 사용
- `NetworkStream` 읽기/쓰기는 Actor 외부 Task에서 수행, 결과만 메시지로 전달
- 패킷 분할(fragmentation) 대응 필수 — 헤더 먼저 4바이트 읽고 length 확인 후 payload 읽기
- 향후 Akka.Cluster 도입 시 GatewayActor ↔ ZoneActor 간 통신은 Akka.Remote 메시지로 자동 전환 가능
