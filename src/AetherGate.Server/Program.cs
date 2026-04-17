using Akka.Actor;
using Akka.Configuration;
using AetherGate.Application.Actors;

Console.Title = "AetherGate Server";
Console.WriteLine("========================================");
Console.WriteLine("  AetherGate MMO Server");
Console.WriteLine("  Powered by Akka.NET");
Console.WriteLine("========================================");

// ─── Akka 설정 ────────────────────────────────────────────────────────
var hocon = ConfigurationFactory.ParseString("""
    akka {
        loglevel = INFO
        log-config-on-start = off
        actor {
            provider = remote
        }
        remote {
            dot-netty.tcp {
                hostname = "127.0.0.1"
                port = 8090
            }
        }
    }
    """);

// ─── ActorSystem 생성 ──────────────────────────────────────────────────
using var system = ActorSystem.Create("AetherGate", hocon);

// ─── 최상위 GameServerActor 생성 ──────────────────────────────────────
var gameServer = system.ActorOf(
    Props.Create(() => new GameServerActor()),
    "game-server");

// ─── 서버 시작 명령 전송 ──────────────────────────────────────────────
gameServer.Tell(new GameServerActor.StartServer());

Console.WriteLine("\n[Server] Running. Press Ctrl+C to exit.\n");

// ─── 종료 처리 ────────────────────────────────────────────────────────
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }

Console.WriteLine("\n[Server] Shutting down...");
gameServer.Tell(new GameServerActor.StopServer());
await system.Terminate();
Console.WriteLine("[Server] Goodbye.");
