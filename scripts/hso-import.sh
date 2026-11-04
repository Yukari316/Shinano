#!/bin/sh
# 把目录中的图片导入 Hso 图库：hso-import --dir <目录>；需先在面板启用过一次 Hso 插件。
# 逻辑在 host/Shinano.Tool 中，本脚本只是入口；参数原样传给工具，如 --bucket-prefix。
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
tool="$root/host/Shinano.Tool"
# 在 Windows 上打出的压缩包不一定保留可执行权限。
chmod +x "$tool" "$root/host/Shinano.Host" "$root/monitor/Shinano.Monitor" 2>/dev/null || true
exec "$tool" hso-import "$@"
