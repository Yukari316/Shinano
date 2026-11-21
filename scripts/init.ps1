#Requires -Version 7.0
# 初始化或重新配置本部署：逐项询问部署配置，写入 data/shinano.json。
# 逻辑在 host/Shinano.Tool 中，本脚本只是入口；参数原样传给工具，如 --accept-defaults。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tool = Join-Path $root 'host' ($IsWindows ? 'Shinano.Tool.exe' : 'Shinano.Tool')
if (-not $IsWindows) {
    # 在 Windows 上打出的压缩包不一定保留可执行权限。
    chmod +x (Join-Path $root 'host/Shinano.Tool') (Join-Path $root 'host/Shinano.Host') (Join-Path $root 'monitor/Shinano.Monitor') 2>$null
}
& $tool init @args
exit $LASTEXITCODE
