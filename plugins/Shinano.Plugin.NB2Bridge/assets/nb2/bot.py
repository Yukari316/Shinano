"""Shinano 托管的 NoneBot2 入口。

由 NB2Bridge 每次启动 NB2 进程前覆盖，不要手工修改。进程级配置（监听地址、Milky 连接、SUPERUSERS、要加载的插件等）
全部由 NB2Bridge 经环境变量传入；`.env` 只放插件配置。

设置 `SHINANO_NB2_PROBE=1` 时为试加载：按与正式运行相同的顺序加载全部插件后立即退出，不启动服务；
输出一行 `SHINANO_NB2_PROBE_FAILED=<加载失败的模块 JSON 数组>`，全部加载成功时退出码为 0，否则为 1。
NB2Bridge 在安装、卸载与重建环境后用它确认插件能实际加载。
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
modules = ["shinano_nb2_control", *json.loads(os.environ.get("SHINANO_NB2_PLUGINS", "[]"))]
failed = [module for module in modules if nonebot.load_plugin(module) is None]

if os.environ.get("SHINANO_NB2_PROBE") == "1":
    print("SHINANO_NB2_PROBE_FAILED=" + json.dumps(failed), flush=True)
    sys.exit(1 if failed else 0)

if __name__ == "__main__":
    nonebot.run()
