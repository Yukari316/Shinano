#!/bin/sh
# 生成发布包：artifacts/release/shinano-<版本>-win-x64.zip 与 shinano-<版本>-linux-x64.tar.gz。
# 每个包自带运行时，只含目标平台的原生库；目录为 host/（宿主与工具）、monitor/、bundled-plugins/（插件包 zip）、scripts/。
# 需要 .NET SDK、pnpm、zip 与 tar。版本号取自 Directory.Build.props。用法：build/pack.sh [rid ...]
set -eu
repo=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
release="$repo/artifacts/release"
work="$release/work"
runtimes=${*:-win-x64 linux-x64}

version=$(dotnet msbuild "$repo/src/Shinano.Host/Shinano.Host.csproj" -getProperty:Version | tr -d '[:space:]')
echo "Shinano $version"
rm -rf "$work"
mkdir -p "$work/bundled-plugins"

# 前端：管理面板构建到宿主 wwwroot，状态页构建到监控 wwwroot，自带插件的专有页面构建到各插件包的 web/。
# 面板构建要读取插件 SDK 的 dist/，pnpm 按依赖顺序先构建 SDK。
pnpm -C "$repo/web" install --frozen-lockfile
pnpm -C "$repo/web" --filter '@shinano/plugin-sdk' --filter '@shinano/panel' --filter '@shinano/status' --filter './plugins/*' run build

# 原生插件：与平台无关，打成与面板上传相同的插件包。原生库与共享程序集由宿主提供，不放进插件包。
for project in "$repo"/plugins/*/*.csproj; do
    # 共享类库没有 plugin.json，随引用它的插件一起构建与打包。
    [ -f "$(dirname "$project")/plugin.json" ] || continue
    name=$(basename "$project" .csproj)
    output="$repo/artifacts/plugins/$name"
    # 先删除上次的输出：构建不会清除源码中已删除的文件，残留的文件会被打进插件包。
    rm -rf "$output"
    dotnet build "$project" -c Release
    id=$(sed -n 's/.*"id"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$output/plugin.json" | head -n 1)
    (cd "$output" && zip -qr "$work/bundled-plugins/$id.zip" . -x 'runtimes/*')
done

for rid in $runtimes; do
    name="shinano-$version-$rid"
    package="$work/$name"
    # 工具先于宿主发布到同一目录：两者共用运行时文件，重叠的程序集以宿主（含 ASP.NET Core 框架）的版本为准。
    dotnet publish "$repo/src/Shinano.Tool/Shinano.Tool.csproj" -c Release -r "$rid" --self-contained -o "$package/host"
    dotnet publish "$repo/src/Shinano.Host/Shinano.Host.csproj" -c Release -r "$rid" --self-contained -o "$package/host"
    dotnet publish "$repo/src/Shinano.Monitor/Shinano.Monitor.csproj" -c Release -r "$rid" --self-contained -o "$package/monitor"
    cp -R "$work/bundled-plugins" "$package/bundled-plugins"
    cp -R "$repo/scripts" "$package/scripts"
    chmod +x "$package/scripts/"*.sh

    case "$rid" in
        win-*)
            archive="$release/$name.zip"
            rm -f "$archive"
            (cd "$work" && zip -qr "$archive" "$name")
            ;;
        *)
            archive="$release/$name.tar.gz"
            tar -czf "$archive" -C "$work" "$name"
            ;;
    esac
    echo "已生成 $archive"
done

rm -rf "$work"
