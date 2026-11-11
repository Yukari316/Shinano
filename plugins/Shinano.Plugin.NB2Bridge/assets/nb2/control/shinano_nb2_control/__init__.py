"""Shinano NB2Bridge 的控制插件，随 NB2Bridge 交付并在每次启动前覆盖。

- 消息处理的继续/终止：有 matcher 真正开始运行（权限、规则与全部运行前处理器都已放行，进入 `Matcher.run`）时立即上报「终止」，
  每个事件只报一次，不等处理结束；整个事件没有 matcher 开始运行时，在事件后处理中上报「继续」。经 Milky API 通道调用
  `shinano_event_done`，按 NB2Bridge 附在事件上的 `shinano_key` 关联。
- 在线禁用：被禁用插件的 matcher 在运行前忽略（不阻断其他插件的低优先级 matcher），禁用时销毁其等待中的连续对话；
  定时任务与后台工作不受影响。
- 控制 API：`/shinano/` 下，Bearer token 与 Milky 连接相同，由 NB2Bridge 每次启动时生成。
- 失去 NB2Bridge 连接超过 60 秒时自行退出，避免宿主异常退出后留下孤儿进程。
- `nb2ping` 指令回复 `pong`，用于确认 NB2 在线。
"""

import asyncio
import json
import os
import signal
import sys
import time
from dataclasses import dataclass
from importlib import metadata
from typing import Any

from fastapi import APIRouter, Depends, FastAPI, HTTPException, Request
from pydantic import BaseModel

import nonebot
from nonebot import get_bots, get_driver, get_plugin, logger, on_command
from nonebot.adapters import Bot, Event
from nonebot.exception import IgnoredException
from nonebot.matcher import Matcher, current_matcher, matchers
from nonebot.message import event_postprocessor, run_postprocessor, run_preprocessor
from nonebot.plugin import PluginMetadata, get_plugin_config

__plugin_meta__ = PluginMetadata(
    name="Shinano 控制",
    description="NB2Bridge 的控制插件：事件回执、在线禁用、控制 API 与 nb2ping",
    usage="/nb2ping",
)

TOKEN = os.environ.get("SHINANO_NB2_TOKEN", "")
MANAGED: list[str] = json.loads(os.environ.get("SHINANO_NB2_PLUGINS", "[]"))
EVENT_KEY = "shinano_key"
DONE_ACTION = "shinano_event_done"
ORPHAN_SECONDS = 60
WATCH_SECONDS = 5
SHUTDOWN_DELAY_SECONDS = 0.2
UNATTRIBUTED = ""


@dataclass
class PluginStats:
    events: int = 0
    runs: int = 0
    errors: int = 0
    api_calls: int = 0
    last_error: dict[str, Any] | None = None

    def dump(self) -> dict[str, Any]:
        return {
            "events": self.events,
            "runs": self.runs,
            "errors": self.errors,
            "api_calls": self.api_calls,
            "last_error": self.last_error,
        }


_stats: dict[str, PluginStats] = {}
_disabled: set[str] = set()
# 已上报「终止」的事件；同一事件对象贯穿一次 handle_event。
_reported: set[int] = set()
_background: set[asyncio.Task[None]] = set()


def _root(plugin_id: str | None) -> str:
    """子插件的 ID 为「父:子」，统计与禁用按顶层插件（即加载的模块名）归属。"""
    return plugin_id.split(":")[0] if plugin_id else UNATTRIBUTED


def _stats_of(root: str) -> PluginStats:
    return _stats.setdefault(root, PluginStats())


@run_preprocessor
async def _gate(matcher: Matcher) -> None:
    root = _root(matcher.plugin_id)
    if root in _disabled:
        raise IgnoredException(f"插件 {root} 已被 Shinano 禁用")
    _stats_of(root).events += 1


@run_postprocessor
async def _record(matcher: Matcher, exception: Exception | None) -> None:
    stats = _stats_of(_root(matcher.plugin_id))
    stats.runs += 1
    if exception is not None:
        stats.errors += 1
        stats.last_error = {"type": type(exception).__name__, "message": str(exception), "time": int(time.time())}


async def _send_result(bot: Bot, key: str, handled: bool) -> None:
    try:
        # 适配器只允许调用 Bot 类上定义的 API，回执经底层 HTTP 调用发出。
        await bot._call(DONE_ACTION, {"key": key, "handled": handled})  # type: ignore[attr-defined]
    except Exception as exception:
        logger.warning(f"上报事件处理结果失败：{exception!r}")


_original_run = Matcher.run


# NoneBot 只在权限、规则与全部运行前处理器都放行后才调用 Matcher.run，两者之间没有公开钩子，因此包装它：
# 进入即表示 matcher 确实开始处理该事件。上报放到后台，不推迟 matcher 本身的运行。
async def _run_and_report(self: Matcher, bot: Bot, event: Event, *args: Any, **kwargs: Any) -> None:
    key = getattr(event, EVENT_KEY, None)
    if isinstance(key, str) and id(event) not in _reported:
        _reported.add(id(event))
        task = asyncio.create_task(_send_result(bot, key, True))
        _background.add(task)
        task.add_done_callback(_background.discard)
    await _original_run(self, bot, event, *args, **kwargs)


Matcher.run = _run_and_report  # type: ignore[method-assign]


@event_postprocessor
async def _report_unhandled(bot: Bot, event: Event) -> None:
    if id(event) in _reported:
        _reported.discard(id(event))
        return
    key = getattr(event, EVENT_KEY, None)
    if isinstance(key, str):
        await _send_result(bot, key, False)


@Bot.on_calling_api
async def _count_api(bot: Bot, api: str, data: dict[str, Any]) -> None:
    matcher = current_matcher.get(None)
    _stats_of(_root(matcher.plugin_id) if matcher else UNATTRIBUTED).api_calls += 1


def _apply_disabled(disabled: set[str]) -> None:
    newly_disabled = disabled - _disabled
    _disabled.clear()
    _disabled.update(disabled)
    for bucket in list(matchers.values()):
        for matcher in list(bucket):
            if matcher.temp and _root(matcher.plugin_id) in newly_disabled:
                matcher.destroy()
    if newly_disabled:
        logger.info(f"已禁用并取消等待中的连续对话：{', '.join(sorted(newly_disabled))}")


def _describe(module: str) -> dict[str, Any]:
    plugin = get_plugin(module)
    entry: dict[str, Any] = {
        "module": module,
        "loaded": plugin is not None,
        "enabled": module not in _disabled,
        "name": module,
        "description": "",
        "matchers": 0,
        "config_schema": None,
        "config": None,
        "stats": _stats_of(module).dump(),
    }
    if plugin is None:
        return entry

    entry["matchers"] = sum(1 for bucket in matchers.values() for matcher in bucket if _root(matcher.plugin_id) == module)
    meta = plugin.metadata
    if meta is None:
        return entry

    entry["name"] = meta.name
    entry["description"] = meta.description
    if meta.config is not None:
        entry["config_schema"] = meta.config.model_json_schema()
        entry["config"] = get_plugin_config(meta.config).model_dump(mode="json")
    return entry


async def _authorize(request: Request) -> None:
    if not TOKEN or request.headers.get("authorization") != f"Bearer {TOKEN}":
        raise HTTPException(status_code=401)


class DisabledRequest(BaseModel):
    disabled: list[str]


router = APIRouter(prefix="/shinano", dependencies=[Depends(_authorize)])


@router.get("/plugins")
async def _plugins() -> list[dict[str, Any]]:
    return [_describe(module) for module in MANAGED]


@router.put("/plugins/disabled")
async def _set_disabled(request: DisabledRequest) -> dict[str, Any]:
    _apply_disabled(set(request.disabled))
    return {"disabled": sorted(_disabled)}


@router.get("/status")
async def _status() -> dict[str, Any]:
    return {
        "python": sys.version.split()[0],
        "nonebot": metadata.version("nonebot2"),
        "bots": list(get_bots()),
        "unattributed_api_calls": _stats_of(UNATTRIBUTED).api_calls,
    }


@router.post("/shutdown")
async def _shutdown() -> dict[str, Any]:
    # 先返回响应，再以 SIGINT 触发 uvicorn 的正常关停（driver shutdown hooks 照常执行）。
    asyncio.get_running_loop().call_later(SHUTDOWN_DELAY_SECONDS, signal.raise_signal, signal.SIGINT)
    return {}


app: FastAPI = nonebot.get_app()
app.include_router(router)


async def _watch_orphan() -> None:
    alone_since = time.monotonic()
    while True:
        await asyncio.sleep(WATCH_SECONDS)
        if get_bots():
            alone_since = time.monotonic()
            continue
        if time.monotonic() - alone_since > ORPHAN_SECONDS:
            logger.error(f"超过 {ORPHAN_SECONDS} 秒未连上 NB2Bridge，进程退出")
            signal.raise_signal(signal.SIGINT)
            return


@get_driver().on_startup
async def _start_watch() -> None:
    task = asyncio.create_task(_watch_orphan())
    _background.add(task)
    task.add_done_callback(_background.discard)


_ping = on_command("nb2ping", priority=1, block=True)


@_ping.handle()
async def _pong() -> None:
    await _ping.finish("pong")
