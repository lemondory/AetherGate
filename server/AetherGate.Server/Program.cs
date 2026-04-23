using Akka.Actor;
using Akka.Configuration;
using AetherGate.Application.Actors;
using AetherGate.Infrastructure.Jwt;
using Microsoft.Extensions.Configuration;

Console.Title = "AetherGate Server";
Console.WriteLine("========================================");
Console.WriteLine("  AetherGate MMO Server");
Console.WriteLine("  Powered by Akka.NET");
Console.WriteLine("========================================");

// ─── 설정 로딩 (appsettings.json → 환경변수 우선 적용) ──────────────
// Docker Compose: JWT_SECRET, JWT_ISSUER 등 환경변수로 주입
// 로컬 개발:     appsettings.json 또는 환경변수
var config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var jwtSecret = config["JWT_SECRET"] ?? config["Jwt:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException(
        "JWT secret is required. Set JWT_SECRET env var or Jwt:Secret in appsettings.json");

var jwtIssuer   = config["JWT_ISSUER"]   ?? config["Jwt:Issuer"]   ?? "AetherGate";
var jwtAudience = config["JWT_AUDIENCE"] ?? config["Jwt:Audience"] ?? "AetherGateClient";
var jwtExpiry   = int.TryParse(config["Jwt:ExpiryHours"], out var h) ? h : 24;

var redisConnection = config["REDIS_URL"] ?? config["Redis:ConnectionString"] ?? "localhost:6379";

// ─── JwtTokenService 생성 (DefaultTokenService 대체) ─────────────────
ITokenService tokenService = new JwtTokenService(jwtSecret, jwtIssuer, jwtAudience, jwtExpiry);

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

// ─── 최상위 GameServerActor 생성 (ITokenService + Redis 주입) ─────────
var gameServer = system.ActorOf(
    Props.Create(() => new GameServerActor(tokenService, redisConnection)),
    "game-server");

// ─── 서버 시작 명령 전송 ──────────────────────────────────────────────
gameServer.Tell(new GameServerActor.StartServer());

Console.WriteLine($"\n[Server] Running (JWT issuer={jwtIssuer}, Redis={redisConnection})");
Console.WriteLine("[Server] Press Ctrl+C to exit.\n");

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
