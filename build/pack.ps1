#Requires -Version 7.0
# 生成发布包：artifacts/release/shinano-<版本>-win-x64.zip 与 shinano-<版本>-linux-x64.tar.gz。
# 每个包自带运行时，只含目标平台的原生库；目录为 host/（宿主与工具）、monitor/、bundled-plugins/（插件包 zip）、scripts/。
# 需要 .NET SDK 与 pnpm。版本号取自 Directory.Build.props。
param(
    [string[]] $Runtime = @('win-x64', 'linux-x64')
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$release = Join-Path $repo 'artifacts/release'
$work = Join-Path $release 'work'

# pnpm 等命令可能是 PowerShell 脚本形式的垫片，参数必须以变量展开传递，不能传数组表达式。
function Invoke-Native {
    $command, $rest = $args
    & $command @rest
    if ($LASTEXITCODE -ne 0) { throw "命令失败（退出码 $LASTEXITCODE）：$($args -join ' ')" }
}

# 用 .NET 写 tar.gz：在 Windows 上同样能为 Linux 可执行文件（宿主、工具、监控、createdump 与 .sh 脚本）设置执行权限。
function Write-LinuxArchive([string] $source, [string] $baseName, [string] $archive) {
    $executable = [System.IO.UnixFileMode]'UserRead, UserWrite, UserExecute, GroupRead, GroupExecute, OtherRead, OtherExecute'
    $regular = [System.IO.UnixFileMode]'UserRead, UserWrite, GroupRead, OtherRead'
    $file = [System.IO.File]::Create($archive)
    $gzip = [System.IO.Compression.GZipStream]::new($file, [System.IO.Compression.CompressionLevel]::Optimal)
    $writer = [System.Formats.Tar.TarWriter]::new($gzip, [System.Formats.Tar.TarEntryFormat]::Pax, $false)
    try {
        foreach ($item in Get-ChildItem $source -Recurse -File) {
            $relative = [System.IO.Path]::GetRelativePath($source, $item.FullName).Replace([char]92, [char]'/')
            $entry = [System.Formats.Tar.PaxTarEntry]::new([System.Formats.Tar.TarEntryType]::RegularFile, "$baseName/$relative")
            $isProgram = ($relative -match '^(host|monitor)/(Shinano\.(Host|Tool|Monitor)|createdump)$') -or $relative.EndsWith('.sh')
            $entry.Mode = $isProgram ? $executable : $regular
            $entry.DataStream = [System.IO.File]::OpenRead($item.FullName)
            try { $writer.WriteEntry($entry) } finally { $entry.DataStream.Dispose() }
        }
    }
    finally {
        $writer.Dispose()
        $gzip.Dispose()
        $file.Dispose()
    }
}

$version = (Invoke-Native dotnet msbuild (Join-Path $repo 'src/Shinano.Host/Shinano.Host.csproj') '-getProperty:Version' | Out-String).Trim()
Write-Host "Shinano $version"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null

# 前端：管理面板构建到宿主 wwwroot，状态页构建到监控 wwwroot，自带插件的专有页面构建到各插件包的 web/。
Invoke-Native pnpm -C (Join-Path $repo 'web') install --frozen-lockfile
Invoke-Native pnpm -C (Join-Path $repo 'web') --filter '@shinano/panel' --filter '@shinano/status' --filter './plugins/*' run build

# 原生插件：与平台无关，打成与面板上传相同的插件包。原生库与共享程序集由宿主提供，不放进插件包。
$plugins = Join-Path $work 'bundled-plugins'
New-Item -ItemType Directory -Force $plugins | Out-Null
foreach ($project in Get-ChildItem (Join-Path $repo 'plugins') -Filter '*.csproj' -Recurse) {
    Invoke-Native dotnet build $project.FullName -c Release
    $output = Join-Path $repo "artifacts/plugins/$($project.BaseName)"
    $manifest = Get-Content (Join-Path $output 'plugin.json') -Raw | ConvertFrom-Json
    $content = Join-Path $work "plugin-$($manifest.id)"
    Copy-Item $output $content -Recurse
    Remove-Item (Join-Path $content 'runtimes') -Recurse -Force -ErrorAction SilentlyContinue
    [System.IO.Compression.ZipFile]::CreateFromDirectory($content, (Join-Path $plugins "$($manifest.id).zip"))
    Remove-Item $content -Recurse -Force
}

foreach ($rid in $Runtime) {
    $name = "shinano-$version-$rid"
    $package = Join-Path $work $name
    # 工具先于宿主发布到同一目录：两者共用运行时文件，重叠的程序集以宿主（含 ASP.NET Core 框架）的版本为准。
    Invoke-Native dotnet publish (Join-Path $repo 'src/Shinano.Tool/Shinano.Tool.csproj') -c Release -r $rid --self-contained -o (Join-Path $package 'host')
    Invoke-Native dotnet publish (Join-Path $repo 'src/Shinano.Host/Shinano.Host.csproj') -c Release -r $rid --self-contained -o (Join-Path $package 'host')
    Invoke-Native dotnet publish (Join-Path $repo 'src/Shinano.Monitor/Shinano.Monitor.csproj') -c Release -r $rid --self-contained -o (Join-Path $package 'monitor')
    Copy-Item $plugins (Join-Path $package 'bundled-plugins') -Recurse
    Copy-Item (Join-Path $repo 'scripts') (Join-Path $package 'scripts') -Recurse

    if ($rid.StartsWith('win')) {
        $archive = Join-Path $release "$name.zip"
        if (Test-Path $archive) { Remove-Item $archive -Force }
        [System.IO.Compression.ZipFile]::CreateFromDirectory($package, $archive, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    }
    else {
        $archive = Join-Path $release "$name.tar.gz"
        Write-LinuxArchive $package $name $archive
    }

    Write-Host "已生成 $archive（$([math]::Round((Get-Item $archive).Length / 1MB, 1)) MiB）"
}

Remove-Item $work -Recurse -Force
