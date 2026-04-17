using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;
using AetherGate.Domain.Messages.Events;

namespace AetherGate.Application.Actors.Zone;

/// <summary>
/// Zone 내 채팅 브로드캐스트 담당.
/// - 일반 채팅: Zone 내 전체 플레이어에게 브로드캐스트
/// - 귓속말: 특정 플레이어에게만 전달
/// ZoneActor와 독립된 Actor로 분리 → 채팅 장애가 게임에 영향 없음.
/// </summary>
public sealed class ChatActor : ReceiveActor
{
    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly string _zoneId;
    private readonly List<ChatBroadcast> _recentMessages = new();
    private const int MaxHistoryCount = 100;

    public ChatActor(string zoneId)
    {
        _zoneId = zoneId;

        Receive<SendChatCommand>(HandleChat);
        Receive<GetChatHistory>(_ => Sender.Tell(new ChatHistory(_recentMessages.ToList())));
    }

    private void HandleChat(SendChatCommand cmd)
    {
        bool isWhisper = cmd.WhisperTargetId is not null;

        var broadcast = new ChatBroadcast(
            cmd.PlayerId,
            cmd.Message,
            isWhisper,
            cmd.WhisperTargetId);

        // 히스토리 저장
        _recentMessages.Add(broadcast);
        if (_recentMessages.Count > MaxHistoryCount)
            _recentMessages.RemoveAt(0);

        if (isWhisper)
        {
            // 귓속말: ZoneActor를 통해 특정 PlayerActor에 전달
            _log.Debug("[Chat:{0}] Whisper {1} → {2}: {3}",
                _zoneId, cmd.PlayerId, cmd.WhisperTargetId, cmd.Message);
            Context.Parent.Tell(new WhisperMessage(
                cmd.PlayerId, cmd.WhisperTargetId!, broadcast));
        }
        else
        {
            // 존 채팅: ZoneActor가 브로드캐스트
            _log.Debug("[Chat:{0}] Zone chat from {1}: {2}", _zoneId, cmd.PlayerId, cmd.Message);
            Context.Parent.Tell(broadcast);
        }
    }

    public sealed record GetChatHistory;
    public sealed record ChatHistory(List<ChatBroadcast> Messages);
    public sealed record WhisperMessage(string SenderId, string TargetId, ChatBroadcast Broadcast);
}
