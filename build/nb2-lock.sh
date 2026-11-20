#!/bin/sh
# 生成 NB2Bridge 随包的锁定文件：plugins/Shinano.Plugin.NB2Bridge/assets/nb2/ 下的 base.lock（NB2 基础环境）
# 与 initial-plugins.lock（首次建立时随附的插件，以 base.lock 为约束）。跨平台解析、带哈希、禁止预发布版，
# 只考虑 exclude-newer 之前发布的版本，同一日期可以重新生成出同样的结果。
# 跨平台解析与按本机解析的结果可能不同，生成后在临时 venv 中按锁定文件安装，并以 bot.py 的试加载模式实际加载 parser；
# 加载失败时脚本以失败退出，锁定文件不可用。
# 修改锁定文件后须提高 NB2Bridge 的 plugin.json 版本号，否则升级时随包插件不会更新。需要 uv。
# 用法：build/nb2-lock.sh [2026-11-20T00:00:00Z]
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
assets="$repo/plugins/Shinano.Plugin.NB2Bridge/assets/nb2"
exclude_newer=${1:-$(date -u +%Y-%m-%dT00:00:00Z)}

# 在资产目录中以相对路径执行，锁定文件头部记录的命令不含本机路径。
(
    cd "$assets"
    uv pip compile base.in --universal --python-version 3.12 --generate-hashes --prerelease disallow --exclude-newer "$exclude_newer" -o base.lock
    uv pip compile initial-plugins.in -c base.lock --universal --python-version 3.12 --generate-hashes --prerelease disallow --exclude-newer "$exclude_newer" -o initial-plugins.lock
)

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
uv venv "$work/.venv" --python 3.12
python="$work/.venv/bin/python"
uv pip sync --python "$python" --require-hashes "$assets/base.lock" "$assets/initial-plugins.lock"

(
    cd "$work"
    DRIVER='~fastapi+~httpx+~websockets' \
    SHINANO_NB2_PROBE=1 \
    SHINANO_NB2_PLUGINS='["nonebot_plugin_parser"]' \
    LOCALSTORE_DATA_DIR="$work/store/data" \
    LOCALSTORE_CONFIG_DIR="$work/store/config" \
    LOCALSTORE_CACHE_DIR="$work/store/cache" \
    PYTHONUTF8=1 \
    "$python" "$assets/bot.py"
)
echo '锁定文件已生成，并在本机按锁定文件安装、加载验证通过'
