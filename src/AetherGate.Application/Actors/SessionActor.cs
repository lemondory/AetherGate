using System.Net.Sockets;
using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;
using AetherGate.Domain.Network;
using AetherGate.Domain.Network.Packets;
using AetherGate.Domain.ValueObjects;
using AetherGate.Application.Network;

namespace AetherGate.Application.Actors;

/// <summary>
/// 플레이어 1명의 TCP 연결을 담당하는 Actor.
///
/// 수신: ReceiveLoop(별도 Task) → PipeTo(Self) → PacketReceived 메시지 처리
/// 송신: 게임 이벤트 수신 → 직렬화 → NetworkStream.WriteAsync (별도 Task)
///
/// 상태: Unauthenticated → Authenticated (Become 패턴)
/// </summary>
public sealed class SessionActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();

    private readonly string _sessionId;
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly IActorRef _authActor;
    private readonly IActorRef _worldActor;
    private readonly CancellationTokenSource _cts = new();

    private string? _playerId;
    private string? _currentZoneId;
    private ushort _sendSequence = 0;

    public SessionActor(string sessionId, TcpClient tcpClient,
        IActorRef authActor, IActorRef worldActor)
    {
        _sessionId  = sessionId;
        _tcpClient  = tcpClient;
        _stream     = tcpClient.GetStream();
        _authActor  = authActor;
        _worldActor = worldActor;

        BecomeUnauthenticated();
        StartReceiveLoop();
    }

    // ─────────────────────────────────────────────────────────────────
    // 수신 루프 — 별도 Task에서 실행, PipeTo로 메시지화
    // ─────────────────────────────────────────────────────────────────
    private void StartReceiveLoop()
    {
        PacketFramer.ReadPacketAsync(_stream, _cts.Token)
            .PipeTo(Self,
                success: result => result.HasValue
                    ? (object)new PacketReceived(result.Value.Header, result.Value.Payload)
                    : new ConnectionClosed(),
                failure: ex => new ConnectionError(ex.Message));
    }

    // ─────────────────────────────────────────────────────────────────
    // STATE: Unauthenticated — Login 패킷만 허용
    // ─────────────────────────────────────────────────────────────────
    private void BecomeUnauthenticated()
    {
        Become(() =>
        {
            Receive<PacketReceived>(msg =>
            {
                if (msg.Header.PacketId == PacketId.Login)
                {
                    var packet = PacketSerializer.Deserialize<LoginPacket>(msg.Payload);
                    _authActor.Tell(new LoginRequest(packet.Username, packet.PasswordHash));
                }
                else
                {
                    SendPacket(PacketId.Error,
                        new ErrorPacket { Message = "Not authenticated", Code = 401 });
                }
                StartReceiveLoop(); // 다음 패킷 대기
            });

            Receive<LoginSucceeded>(msg =>
            {
                _playerId = msg.PlayerId;
                _log.Info("[Session:{0}] Authenticated as {1}", _sessionId, _playerId);

                SendPacket(PacketId.LoginResult, new LoginResultPacket
                {
                    Success  = true,
                    PlayerId = msg.PlayerId,
                    Token    = msg.Token,
                });

                BecomeAuthenticated();

                // 기본 필드맵 입장
                // WorldActor가 SessionEnrollment를 ZoneActor에 별도 전달하므로 Self 등록 불필요
                _currentZoneId = "field_01";
                _worldActor.Tell(new EnterZoneRequest(
                    _playerId, _currentZoneId, new Position(100, 100)));
            });

            Receive<LoginFailed>(msg =>
            {
                _log.Warning("[Session:{0}] Login failed: {1}", _sessionId, msg.Reason);
                SendPacket(PacketId.LoginResult, new LoginResultPacket
                {
                    Success      = false,
                    ErrorMessage = msg.Reason,
                });
            });

            Receive<ConnectionClosed>(_ => HandleDisconnect());
            Receive<ConnectionError>(msg =>
            {
                _log.Warning("[Session:{0}] Receive error: {1}", _sessionId, msg.Reason);
                HandleDisconnect();
            });
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // STATE: Authenticated — 모든 게임 패킷 처리
    // ─────────────────────────────────────────────────────────────────
    private void BecomeAuthenticated()
    {
        Become(() =>
        {
            Receive<PacketReceived>(msg =>
            {
                RouteInboundPacket(msg.Header.PacketId, msg.Payload);
                StartReceiveLoop(); // 다음 패킷 대기
            });

            // ─── Zone 이벤트 → 클라이언트로 전송 ─────────────────────
            Receive<PlayerEnteredZone>(evt => SendPacket(PacketId.PlayerEntered,
                new PlayerEnteredPacket
                {
                    PlayerId = evt.PlayerId, X = evt.Position.X, Y = evt.Position.Y
                }));

            Receive<PlayerLeftZone>(evt => SendPacket(PacketId.PlayerLeft,
                new PlayerLeftPacket { PlayerId = evt.PlayerId }));

            Receive<PlayerMoved>(evt => SendPacket(PacketId.PlayerMoved,
                new PlayerMovedPacket
                {
                    PlayerId = evt.PlayerId, X = evt.To.X, Y = evt.To.Y
                }));

            Receive<MonsterSpawned>(evt => SendPacket(PacketId.MonsterSpawned,
                new MonsterSpawnedPacket
                {
                    MonsterId  = evt.MonsterId, TemplateId = evt.TemplateId,
                    X = evt.Position.X, Y = evt.Position.Y
                }));

            Receive<MonsterMoved>(evt => SendPacket(PacketId.MonsterMoved,
                new MonsterMovedPacket
                {
                    MonsterId = evt.MonsterId, X = evt.Position.X, Y = evt.Position.Y
                }));

            Receive<MonsterStateChanged>(evt => SendPacket(PacketId.MonsterState,
                new MonsterStatePacket
                {
                    MonsterId = evt.MonsterId, State = (int)evt.NewState
                }));

            Receive<MonsterDied>(evt => SendPacket(PacketId.MonsterDied,
                new MonsterDiedPacket
                {
                    MonsterId = evt.MonsterId, KillerPlayerId = evt.KillerPlayerId
                }));

            Receive<CombatDamage>(evt => SendPacket(PacketId.CombatDamage,
                new CombatDamagePacket
                {
                    AttackerId = evt.AttackerId, TargetId   = evt.TargetId,
                    Damage     = evt.Damage,     IsCritical = evt.IsCritical
                }));

            Receive<SkillUsed>(evt => SendPacket(PacketId.SkillUsed,
                new SkillUsedPacket
                {
                    CasterId = evt.CasterId, SkillId  = evt.SkillId,
                    TargetId = evt.TargetId,
                    TargetX  = evt.TargetPosition?.X,
                    TargetY  = evt.TargetPosition?.Y,
                }));

            Receive<BuffApplied>(evt => SendPacket(PacketId.BuffApplied,
                new BuffAppliedPacket
                {
                    PlayerId   = evt.PlayerId,
                    SkillId    = evt.SkillId,
                    DurationMs = evt.DurationMs,
                }));

            Receive<ItemDropped>(evt => SendPacket(PacketId.ItemDropped,
                new ItemDroppedPacket
                {
                    DropId   = evt.DropId,   ItemId   = evt.ItemId,
                    Quantity = evt.Quantity, X = evt.Position.X, Y = evt.Position.Y
                }));

            Receive<ItemPickedUp>(evt => SendPacket(PacketId.ItemPickedUp,
                new ItemPickedUpPacket
                {
                    PlayerId = evt.PlayerId, DropId   = evt.DropId,
                    ItemId   = evt.ItemId,   Quantity = evt.Quantity,
                }));

            Receive<ItemEnhanced>(evt => SendPacket(PacketId.ItemEnhanced,
                new ItemEnhancedPacket
                {
                    PlayerId = evt.PlayerId, ItemId   = evt.ItemId,
                    NewLevel = evt.NewLevel, Success  = evt.Success,
                }));

            Receive<GoldChanged>(evt =>
            {
                // 본인 것만 전송
                if (evt.PlayerId == _playerId)
                    SendPacket(PacketId.GoldChanged, new GoldChangedPacket
                    {
                        Delta = evt.Delta, NewTotal = evt.NewTotal
                    });
            });

            Receive<ChatBroadcast>(evt => SendPacket(PacketId.ChatMessage,
                new ChatMessagePacket
                {
                    SenderName = evt.SenderName,
                    Message    = evt.Message,
                    IsWhisper  = evt.IsWhisper,
                }));

            Receive<DungeonInstanceCreated>(evt => SendPacket(PacketId.DungeonCreated,
                new DungeonCreatedPacket { ZoneInstanceId = evt.ZoneInstanceId }));

            Receive<DungeonCleared>(evt => SendPacket(PacketId.DungeonResult,
                new DungeonResultPacket { ZoneInstanceId = evt.ZoneInstanceId, IsCleared = true }));

            Receive<DungeonFailed>(evt => SendPacket(PacketId.DungeonResult,
                new DungeonResultPacket { ZoneInstanceId = evt.ZoneInstanceId, IsCleared = false }));

            Receive<ConnectionClosed>(_ => HandleDisconnect());
            Receive<ConnectionError>(msg =>
            {
                _log.Warning("[Session:{0}] Receive error: {1}", _sessionId, msg.Reason);
                HandleDisconnect();
            });
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // 인바운드 패킷 → Command 변환 후 라우팅
    // ─────────────────────────────────────────────────────────────────
    private void RouteInboundPacket(PacketId id, byte[] payload)
    {
        switch (id)
        {
            case PacketId.Move:
            {
                var p = PacketSerializer.Deserialize<MovePacket>(payload);
                _worldActor.Tell(new MoveCommand(_playerId!, new Position(p.X, p.Y)));
                break;
            }
            case PacketId.UseSkill:
            {
                var p = PacketSerializer.Deserialize<UseSkillPacket>(payload);
                Position? targetPos = (p.TargetX.HasValue && p.TargetY.HasValue)
                    ? new Position(p.TargetX.Value, p.TargetY.Value)
                    : null;
                _worldActor.Tell(new UseSkillCommand(_playerId!, p.SkillId, p.TargetId, targetPos));
                break;
            }
            case PacketId.PickupItem:
            {
                var p = PacketSerializer.Deserialize<PickupItemPacket>(payload);
                _worldActor.Tell(new PickupItemCommand(_playerId!, p.DropId));
                break;
            }
            case PacketId.UseItem:
            {
                var p = PacketSerializer.Deserialize<UseItemPacket>(payload);
                _worldActor.Tell(new UseItemCommand(_playerId!, p.ItemId));
                break;
            }
            case PacketId.EnhanceItem:
            {
                var p = PacketSerializer.Deserialize<EnhanceItemPacket>(payload);
                _worldActor.Tell(new EnhanceItemCommand(_playerId!, p.ItemId));
                break;
            }
            case PacketId.BuyItem:
            {
                var p = PacketSerializer.Deserialize<BuyItemPacket>(payload);
                _worldActor.Tell(new BuyItemCommand(_playerId!, p.ItemId, p.Quantity));
                break;
            }
            case PacketId.ZoneChat:
            {
                var p = PacketSerializer.Deserialize<ZoneChatPacket>(payload);
                _worldActor.Tell(new SendChatCommand(_playerId!, p.Message));
                break;
            }
            case PacketId.Whisper:
            {
                var p = PacketSerializer.Deserialize<WhisperPacket>(payload);
                _worldActor.Tell(new SendChatCommand(_playerId!, p.Message, p.TargetName));
                break;
            }
            case PacketId.EnterDungeon:
            {
                var p = PacketSerializer.Deserialize<EnterDungeonPacket>(payload);
                _worldActor.Tell(new EnterDungeonRequest(_playerId!, p.DungeonTemplateId));
                break;
            }
            default:
                _log.Warning("[Session:{0}] Unknown PacketId: {1}", _sessionId, id);
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // 송신 — 직렬화 후 비동기 Write (PipeTo로 오류 처리)
    // ─────────────────────────────────────────────────────────────────
    private void SendPacket<T>(PacketId packetId, T payload)
    {
        var data = PacketSerializer.Serialize(payload);
        var seq  = _sendSequence++;

        PacketFramer.WritePacketAsync(_stream, packetId, data, seq, _cts.Token)
            .PipeTo(Self,
                success: () => Done.Instance,
                failure: ex  => new SendFailed(ex.Message));
    }

    private void HandleDisconnect()
    {
        _log.Info("[Session:{0}] Disconnected (player={1})", _sessionId, _playerId ?? "?");

        if (_playerId is not null && _currentZoneId is not null)
            _worldActor.Tell(new LeaveZoneRequest(_playerId, _currentZoneId));

        Context.Parent.Tell(new ClientDisconnected(_sessionId));
        Context.Stop(Self);
    }

    protected override void PostStop()
    {
        _cts.Cancel();
        _cts.Dispose();
        _tcpClient.Dispose();
    }

    // ─── 내부 메시지 ──────────────────────────────────────────────────
    private sealed record PacketReceived(PacketHeader Header, byte[] Payload);
    private sealed record ConnectionClosed;
    private sealed record ConnectionError(string Reason);
    private sealed record SendFailed(string Reason);
    private sealed record Done
    {
        public static readonly Done Instance = new();
    }
}
