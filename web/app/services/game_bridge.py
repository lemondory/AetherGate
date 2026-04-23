import json

import redis.asyncio as aioredis

from app.core.config import get_settings

_redis_client: aioredis.Redis | None = None

ADMIN_CHANNEL = "admin:commands"

# .NET AdminCommandType enum 값과 일치해야 함 (camelCase JSON)
_CMD_KICK      = "kick"
_CMD_BROADCAST = "broadcast"


async def get_redis() -> aioredis.Redis:
    global _redis_client
    if _redis_client is None:
        _redis_client = aioredis.from_url(
            get_settings().redis_url, decode_responses=True
        )
    return _redis_client


async def kick_player(player_id: str) -> None:
    """Redis Pub/Sub → .NET AdminBridgeActor → GameServerActor → WorldActor.KickPlayerRequest"""
    r = await get_redis()
    await r.publish(ADMIN_CHANNEL, json.dumps({"type": _CMD_KICK, "playerId": player_id}))


async def broadcast_message(message: str) -> None:
    """Redis Pub/Sub → .NET AdminBridgeActor → 모든 ZoneActor → [SYSTEM] 채팅"""
    r = await get_redis()
    await r.publish(ADMIN_CHANNEL, json.dumps({"type": _CMD_BROADCAST, "message": message}))
