#Requires -Version 7.0
# 生成 NB2Bridge 随包的锁定文件：plugins/Shinano.Plugin.NB2Bridge/assets/nb2/ 下的 base.lock（NB2 基础环境）
# 与 initial-plugins.lock（首次建立时随附的插件，以 base.lock 为约束）。跨平台解析、带哈希、禁止预发布版，
# 只考虑 ExcludeNewer 之前发布的版本，同一日期可以重新生成出同样的结果。
# 跨平台解析与按本机解析的结果可能不同，生成后在临时 venv 中按锁定文件安装，并以 bot.py 的试加载模式实际加载 parser；
# 加载失败时脚本以失败退出，锁定文件不可用。Linux 上的安装由另一环境验证。
# 修改锁定文件后须提高 NB2Bridge 的 plugin.json 版本号，否则升级时随包插件不会更新。需要 uv。
# 用法：build/nb2-lock.ps1 [-ExcludeNewer 2026-11-20T00:00:00Z]
param(
    [string] $ExcludeNewer = [DateTime]::UtcNow.ToString('yyyy-MM-ddT00:00:00Z')
)

$ErrorActionPreference = 'Stop'
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) 'plugins/Shinano.Plugin.NB2Bridge/assets/nb2'

function Invoke-Native {
    $command, $rest = $args
    # 只有一个参数时 $rest 是字符串，展开前先转为数组。
    $rest = @($rest)
    & $command @rest
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出码 $LASTEXITCODE）：$($args -join ' ')" }
}

# 在资产目录中以相对路径执行，锁定文件头部记录的命令不含本机路径。
$common = @('--universal', '--python-version', '3.12', '--generate-hashes', '--prerelease', 'disallow', '--exclude-newer', $ExcludeNewer)
Push-Location $assets
try {
    Invoke-Native uv pip compile base.in @common -o base.lock
    Invoke-Native uv pip compile initial-plugins.in -c base.lock @common -o initial-plugins.lock
}
finally {
    Pop-Location
}

$work = Join-Path ([System.IO.Path]::GetTempPath()) "shinano-nb2-lock-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $work | Out-Null
try {
    $venv = Join-Path $work '.venv'
    Invoke-Native uv venv $venv --python 3.12
    $python = if ($IsWindows) { Join-Path $venv 'Scripts/python.exe' } else { Join-Path $venv 'bin/python' }
    Invoke-Native uv pip sync --python $python --require-hashes (Join-Path $assets 'base.lock') (Join-Path $assets 'initial-plugins.lock')

    $env:DRIVER = '~fastapi+~httpx+~websockets'
    $env:SHINANO_NB2_PROBE = '1'
    $env:SHINANO_NB2_PLUGINS = '["nonebot_plugin_parser"]'
    $env:LOCALSTORE_DATA_DIR = Join-Path $work 'store/data'
    $env:LOCALSTORE_CONFIG_DIR = Join-Path $work 'store/config'
    $env:LOCALSTORE_CACHE_DIR = Join-Path $work 'store/cache'
    $env:PYTHONUTF8 = '1'
    Push-Location $work
    try {
        Invoke-Native $python (Join-Path $assets 'bot.py')
    }
    finally {
        Pop-Location
    }
    Write-Host '锁定文件已生成，并在本机按锁定文件安装、加载验证通过'
}
finally {
    Remove-Item -Recurse -Force $work
}
