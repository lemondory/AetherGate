namespace AetherGate.Application.Actors.Zone;

public sealed record ZoneConfig(
    string ZoneId,
    string MapId,
    bool IsInstance,
    int TickIntervalMs = 100,
    int MaxPlayers = 100,
    int MonsterSpawnCount = 10);
