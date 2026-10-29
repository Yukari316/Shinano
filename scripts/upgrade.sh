#!/bin/sh
# 用本发布包升级现有部署：在另一个目录解压新包后运行 upgrade --target <现有部署根目录>。
# 逻辑在 host/Shinano.Tool 中，本脚本只是入口；参数原样传给工具，如 --accept-defaults。
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
tool="$root/host/Shinano.Tool"
# 在 Windows 上打出的压缩包不一定保留可执行权限。
chmod +x "$tool" "$root/host/Shinano.Host" "$root/monitor/Shinano.Monitor" 2>/dev/null || true
exec "$tool" upgrade "$@"
