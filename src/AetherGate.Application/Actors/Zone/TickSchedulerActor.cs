using Akka.Actor;
using Akka.Event;
using AetherGate.Domain.Messages.Commands;

namespace AetherGate.Application.Actors.Zone;

/// <summary>
/// Zone 내부의 일정 주기 Tick을 ZoneActor에게 전달.
/// Akka Scheduler를 사용해 시스템 스레드 블로킹 없이 동작.
/// </summary>
public sealed class TickSchedulerActor : ReceiveActor, IWithTimers
{
    public ITimerScheduler Timers { get; set; } = null!;

    private readonly ILoggingAdapter _log = Context.GetLogger();
    private readonly IActorRef _zoneActor;
    private readonly int _intervalMs;
    private long _tickNumber = 0;

    private static readonly object TickKey = new();

    public TickSchedulerActor(IActorRef zoneActor, int intervalMs = 100)
    {
        _zoneActor = zoneActor;
        _intervalMs = intervalMs;

        Receive<InternalTick>(_ => HandleTick());
        Receive<PauseScheduler>(_ => Timers.Cancel(TickKey));
        Receive<ResumeScheduler>(_ => StartTimer());
    }

    protected override void PreStart()
    {
        StartTimer();
        _log.Debug("[TickScheduler] Started with {0}ms interval", _intervalMs);
    }

    private void StartTimer()
    {
        Timers.StartPeriodicTimer(
            TickKey,
            new InternalTick(),
            TimeSpan.FromMilliseconds(_intervalMs));
    }

    private void HandleTick()
    {
        _tickNumber++;
        _zoneActor.Tell(new Tick(_tickNumber, _intervalMs));
    }

    protected override void PostStop()
    {
        _log.Debug("[TickScheduler] Stopped.");
    }

    private sealed record InternalTick;
    public sealed record PauseScheduler;
    public sealed record ResumeScheduler;
}
