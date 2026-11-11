"""Shinano 托管的 NoneBot2 入口。

由 NB2Bridge 每次启动 NB2 进程前覆盖，不要手工修改。进程级配置（监听地址、Milky 连接、SUPERUSERS、要加载的插件等）
全部由 NB2Bridge 经环境变量传入；`.env` 只放插件配置。
"""

import json
import os
import sys
from pathlib import Path

import nonebot
from nonebot.adapters.milky import Adapter as MilkyAdapter

sys.path.insert(0, str(Path(__file__).parent / "control"))

nonebot.init()
nonebot.get_driver().register_adapter(MilkyAdapter)
# 控制插件最先加载：事件回执、在线禁用与控制 API 不依赖其他插件。
nonebot.load_plugin("shinano_nb2_control")
for module in json.loads(os.environ.get("SHINANO_NB2_PLUGINS", "[]")):
    nonebot.load_plugin(module)

if __name__ == "__main__":
    nonebot.run()
