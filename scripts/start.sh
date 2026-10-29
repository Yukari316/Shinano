#!/bin/sh
# 直接运行 Shinano：首次运行时先初始化，然后在前台守护宿主与监控，按 Ctrl+C 停止。
# 逻辑在 host/Shinano.Tool 中，本脚本只是入口；参数原样传给工具，如 --accept-defaults。
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
tool="$root/host/Shinano.Tool"
# 在 Windows 上打出的压缩包不一定保留可执行权限。
chmod +x "$tool" "$root/host/Shinano.Host" "$root/monitor/Shinano.Monitor" 2>/dev/null || true
if [ ! -f "$root/data/shinano.local.json" ]; then
    "$tool" init --no-start
fi
exec "$tool" run "$@"
