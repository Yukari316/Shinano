# Shinano

Shinano 是基于 [Sora](https://github.com/Hoshikawa-Kaguya/Sora)（Milky 协议）的 QQ 机器人，使用 .NET 10。Bot、管理 API 与管理面板运行在同一个进程中；另有独立的监控进程（服务状态页）和运维工具。业务功能全部以插件提供，在管理面板中启用、停用、排序和配置。

## 功能

宿主本身不含业务功能，提供：

- **插件运行时**：每个插件使用独立的加载上下文。启用、停用和排序由宿主统一控制。插件包经面板上传，重启后生效；新版本启动失败时自动回滚。
- **聊天记录与统计**：保存群聊和私聊的完整正文与媒体，按会话、发言者和自然日统计发言条数与字数，过期自动清理。
- **运行状态与通知**：数据库离线时暂停全部插件；RustFS 离线时降级运行。故障开始和恢复时，各向 Super User 私聊发送一次通知卡片。
- **管理面板**：总览、插件管理与配置、统计与聊天数据、实时日志与历史日志、宿主配置、外观。
- **监控进程**：独立的只读状态页，在主进程停止时仍能显示 PostgreSQL、RustFS 与 LLBot 的可用率。
- **公共能力**：消息图片卡片、公共字体、无头浏览器、ffmpeg、调度器与媒体存储，供插件使用。

随包插件（首次部署时装入，默认不启用）：

| 插件 | 说明 |
|---|---|
| 在线检测 | 私聊「在」、戳一戳与 echo，用于确认 Bot 在线 |
| 娱乐 | 骰子、随机选择与娱乐禁言 |
| 切噜 | 文本与切噜语互相转换 |
| 有人问 | 群内问答：按问题自动回复文字、图片、语音、视频或文件 |
| 搜图 | 以图搜图（SauceNAO，IQDB 兜底）与以图搜番（trace.moe），需要图源服务 |
| Hso 图库 | 群内随机发送本地图库中的图片与作品信息，需要图源服务 |
| B站订阅 | 推送关注的 UP 主的动态卡片与开播、下播提醒，扫码登录 |
| NB2Bridge | 托管 NoneBot2 服务（含链接解析插件 nonebot-plugin-parser），可安装其他 NB2 插件 |

## 运行要求

- **PostgreSQL 18 及以上**：编码 UTF8，支持 lz4 压缩。Bot 使用非超级用户的业务角色。
- **RustFS** 或其他兼容 S3 的对象存储。
- **Milky 协议端**（如 LLBot），已登录机器人账号并开启 Milky。
- **系统**：Windows x64 或 Linux x64。发布包自带 .NET 运行时。
- **可选**：使用 NB2Bridge 时需要 [uv](https://docs.astral.sh/uv/)。ffmpeg 与浏览器（Chrome for Testing）默认在首次使用时自动下载。

## 快速开始

1. 从发布页下载对应平台的发布包（`shinano-<版本>-win-x64.zip` 或 `shinano-<版本>-linux-x64.tar.gz`），解压到程序目录。
2. 运行初始化脚本，按提示填写数据库、RustFS、LLBot、Super User 与面板监听地址：
   - Windows（PowerShell 7）：`scripts/init.ps1`
   - Linux：`scripts/init.sh`
3. 按初始化选择的运行方式启动：直接运行用 `scripts/start.ps1` 或 `scripts/start.sh`；服务模式下，初始化时已创建服务。
4. 用初始化结束时显示的 Token 登录管理面板，在插件管理页启用需要的插件。

详细步骤与运维说明见 [部署与运维](docs/deployment.md)，插件开发见 [插件开发](docs/plugin-development.md)。术语见 [GLOSSARY.md](GLOSSARY.md)。

## 开发

需要 .NET SDK 10、Node.js 与 pnpm（版本见 `web/package.json` 的 `packageManager`）。

```sh
# 后端：插件输出到 artifacts/plugins/，测试插件输出到 artifacts/test-plugins/
dotnet build Shinano.slnx

# 前端：面板、状态页、plugin-sdk 与自带插件的页面
pnpm -C web install
pnpm -C web --filter @shinano/plugin-sdk --filter @shinano/panel --filter @shinano/status --filter "./plugins/*" run build

# 测试
dotnet test --project tests/Shinano.Tests
```

集成测试需要环境变量 `SHINANO_TEST_DSN`、`SHINANO_TEST_S3_ENDPOINT`、`SHINANO_TEST_S3_ACCESS_KEY`、`SHINANO_TEST_S3_SECRET_KEY`，未设置时跳过。集成测试在指定的库中按次创建临时 schema，并创建 `shinano-debug-test-*` bucket，结束后删除。

生成发布包：`build/pack.ps1` 或 `build/pack.sh`，输出到 `artifacts/release/`。版本号取自 `Directory.Build.props`。

## 许可证

[MIT](LICENSE)。第三方组件及其许可证见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
