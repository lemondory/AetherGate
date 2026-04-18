# AetherGate — Test Suite

## Overview

| Tier | Scope | Tests | Status |
|------|-------|-------|--------|
| 1 — Unit | PacketSerializer, PacketFramer, MonsterActor FSM, InventoryItem | 36 | All Pass |
| 1 — Unit | PlayerActor | 13 | All Pass |
| 1 — Unit | AuthActor | 8 | All Pass |
| 2 — Integration | ZoneActor (Akka.TestKit) | 9 | All Pass |
| 3 — E2E | Real TCP socket scenario | 5 | All Pass |
| **Total** | | **80** | **80 / 80** |

## Running Tests

```bash
# All tests
dotnet test

# By tier / component
dotnet test --filter "FullyQualifiedName~Network"      # Packet (Serializer + Framer)
dotnet test --filter "FullyQualifiedName~MonsterActor" # Monster FSM
dotnet test --filter "FullyQualifiedName~PlayerActor"  # Player logic
dotnet test --filter "FullyQualifiedName~AuthActor"    # Auth
dotnet test --filter "FullyQualifiedName~Inventory"    # Domain: InventoryItem
dotnet test --filter "FullyQualifiedName~ZoneActor"    # Integration: Zone
dotnet test --filter "FullyQualifiedName~EndToEnd"     # E2E TCP
```

## Test Scope

### Tier 1 — Unit Tests
- **PacketSerializer**: MessagePack round-trip for all packet types
- **PacketFramer**: TCP frame read/write, fragmentation handling, connection close detection
- **MonsterActor**: FSM state transitions (Patrol → Detect → Chase → Attack → Return), damage/death handling
- **PlayerActor**: Movement, skill use & cooldown, buy/gold, item enhancement, damage/death, gold from kill
- **AuthActor**: Login success/failure, token generation, token validation, multi-login token isolation
- **InventoryItem**: Enhance success rate table, deterministic TryEnhance, TryConsume, Add

### Tier 2 — Integration Tests (Akka.TestKit)
- **ZoneActor**: Player enter/leave broadcast, movement routing, combat broadcast, zone chat, whisper targeting, gold isolation

### Tier 3 — E2E Tests
- Real `TcpListener` / `TcpClient` connection to `GatewayActor`
- Multi-client simultaneous connection
- Graceful disconnect detection
- Large packet (50KB) fragmentation integrity

## Bugs Found During Testing

| # | Component | Description | Fixed |
|---|-----------|-------------|-------|
| 1 | MonsterActor | `Tell()` in constructor before `PreStart` — state message ordering issue | Yes |
| 2 | ZoneActor | Missing `ScanForPlayers` handler — monsters stuck in Patrol forever | Yes |
| 3 | ZoneActor | Missing `WhisperMessage` handler — whisper never delivered | Yes |
