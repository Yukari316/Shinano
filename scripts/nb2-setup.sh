#!/bin/sh
# 用 uv 创建或补全 NB2 运行环境（部署数据目录的 nb2/）；需要已安装 uv。
# 逻辑在 host/Shinano.Tool 中，本脚本只是入口；参数原样传给工具。
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
tool="$root/host/Shinano.Tool"
# 在 Windows 上打出的压缩包不一定保留可执行权限。
chmod +x "$tool" "$root/host/Shinano.Host" "$root/monitor/Shinano.Monitor" 2>/dev/null || true
exec "$tool" nb2-setup "$@"
