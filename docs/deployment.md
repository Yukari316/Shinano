# 部署与运维

本文写给部署和维护 Shinano 的管理员。项目简介见 [README](../README.md)，插件开发见 [插件开发](plugin-development.md)。

文中的地址都是示例（`192.0.2.0/24` 为文档专用地址），路径都相对部署根目录。

## 1. 组件与依赖

一个部署包含以下程序：

| 组件 | 说明 |
|---|---|
| 宿主 `Shinano.Host` | 运行 Bot、管理 API 与管理面板，同一进程 |
| 状态页 `Shinano.Monitor` | 独立进程，只读监视宿主与依赖服务，主进程停止时仍可查看 |
| 工具 `Shinano.Tool` | 初始化、直接运行时的守护、升级、系统服务管理、NB2 环境与 Hso 图库导入；由 `scripts/` 下的脚本调用 |
| 随包插件 | 有人问、B站订阅、搜图、Hso 图库、娱乐、切噜、在线检测、NB2Bridge；首次初始化时装入，默认不启用 |

需要自行准备的外部服务：

- **PostgreSQL 18 及以上**：数据库编码 UTF8，服务器需支持 lz4 列压缩。Bot 使用不是超级用户的业务角色运行。
- **RustFS 或其他 S3 兼容存储**：保存聊天媒体、持久媒体和插件 bucket。IM 协议端下载媒体用的是预签名链接，所以协议端也要能访问存储。
- **Milky 协议端**（如 LLBot）：已登录机器人账号并开启 Milky。一个部署同时只连接一个机器人账号。
- **uv**（只在使用 NB2Bridge 时需要）：用于创建 NoneBot2 的 Python 环境，需在 `PATH` 中。

以下依赖由宿主在首次使用时自动下载到部署数据目录，不计入启动检查与健康状态，不可用时只影响用到它的插件：

- **ffmpeg**：默认从 FFmpeg-Builds 下载静态构建，按 SHA256 校验后用系统的 `tar` 解压到 `data/ffmpeg/`。也可以改用系统 `PATH` 中的 ffmpeg，或指定一个目录（见附录 `Shinano:Ffmpeg`）。目前 NB2 的链接解析会用到它。
- **浏览器**：默认下载最新稳定版 Chrome for Testing 到 `data/browser/`，每天最多检查一次新版本。也可以指定本机浏览器（见附录 `Shinano:Browser`）。目前 B站订阅用它维护登录状态。Linux 上浏览器以 `--no-sandbox` 运行。

## 2. 发布包与部署目录

发布包按平台区分，自带 .NET 运行时，目标机不需要安装 .NET：

- Windows：`shinano-<版本>-win-x64.zip`
- Linux：`shinano-<版本>-linux-x64.tar.gz`

从源码生成发布包需要 .NET SDK、pnpm、zip 与 tar，用法为 `build/pack.ps1` 或 `build/pack.sh [rid ...]`，输出到 `artifacts/release/`。

解压后的目录就是部署根目录：

```text
shinano-<版本>-<平台>/
├── host/              宿主与工具；宿主日志在 host/Logs/
├── monitor/           状态页；日志在 monitor/Logs/，探测记录在 monitor/data/
├── bundled-plugins/   随包插件的插件包（zip）
├── scripts/           init、start、service、upgrade、nb2-setup、hso-import 的 PowerShell 7 与 sh 入口
└── data/              部署数据目录，初始化时创建
```

`host/`、`monitor/`、`bundled-plugins/`、`scripts/` 是程序目录，升级时整体替换。`data/` 是部署数据目录，升级时不动。工具和服务定义通过环境变量 `SHINANO_DATA_DIR` 把它交给宿主与状态页。

部署数据目录中的内容：

| 路径 | 内容 |
|---|---|
| `shinano.local.json` | 本地配置，含全部连接凭据与面板 Token |
| `deployment.json` | 工具记录的运行方式、服务名与已装入的随包插件，宿主不读取 |
| `tool.log` | 工具修改系统状态的操作记录 |
| `plugins/` | 已安装的插件，每个插件一个子目录 |
| `plugin-staging/`、`restart/` | 等待重启应用的插件包变更与重启状态 |
| `plugin-purge/` | 清除插件数据的进度 |
| `secrets/` | 插件机密，每个插件一个 `<插件 ID>.json` |
| `keys/` | 面板登录会话的签名密钥 |
| `panel/` | 面板外观设置与背景图 |
| `browser/` | 下载的浏览器与各插件的浏览器用户目录（含 B站登录状态） |
| `ffmpeg/` | 下载的 ffmpeg |
| `nb2/` | NB2 运行环境、插件配置与日志 |
| `service/` | Linux 上生成的 systemd unit 文件 |

`shinano.local.json`、`secrets/`、`keys/` 与 `browser/` 都包含机密，不要让其他用户读取，也不要提交到版本库或发到聊天中。Linux 上本地配置、插件机密与 NB2 的 `.env` 以 0600 创建，`keys/` 与 `browser/` 由框架和浏览器创建，请确认部署数据目录只有运行用户可以访问；Windows 依靠部署目录的 ACL 限制访问。

## 3. 数据库准备

Bot 使用业务角色连接一个归管理员所有的数据库，角色需要该库的 `CONNECT`、`CREATE`、`TEMP` 权限，不能是超级用户。这样 Bot 可以自行创建 schema 和表，但不能删除数据库或创建扩展。

有两种准备方式：

- **由初始化脚本创建**：初始化时选择用管理员账号创建，输入管理员用户名（默认 `postgres`）和密码。脚本会核验服务器版本与 lz4 支持，创建或更新业务角色（设置密码），用 `template0` 以 UTF8 编码创建数据库，并授予上述权限。管理员密码只在本次使用，不保存，也不记录。
- **由 DBA 事先建好**：数据库编码必须是 UTF8，并授予业务角色上述权限。初始化时跳过管理员步骤即可。

无论哪种方式，初始化都会在保存前用业务角色实际连接，并核验：PostgreSQL 主版本不低于 18、服务器支持 lz4、数据库编码为 UTF8、角色不是超级用户。核验失败时可以选择仍然保存，但宿主启动检查不会通过。

各 schema 和表由 Bot 在启动时自动创建：全局数据在 `global`，机器人账号的数据在 `user_<账号>`。

## 4. 初始化

### 交互式

在部署根目录运行：

```sh
scripts/init.sh        # Linux
```

```powershell
scripts/init.ps1       # Windows，需要 PowerShell 7
```

工具逐项询问，直接回车即使用方括号中的默认值。重复运行时，默认值就是现有配置，未修改的项保持不变。依次询问：

1. **运行方式**：`direct` 为直接运行，`service` 为作为系统服务运行。选择服务时，还要输入宿主和状态页的服务名（默认 `Shinano`、`ShinanoMonitor`）。Linux 上会询问是否以专用用户 `shinano` 运行服务，用户不存在时自动创建。
2. **时区**：默认 `Asia/Shanghai`，决定聊天分区和统计的自然日。
3. **bucket 前缀**：默认 `shinano`，每个部署必须唯一，不要与其他部署共用。
4. **Super User**：QQ 号，多个用逗号分隔。
5. **PostgreSQL**：主机、端口、数据库名（默认 `shinano`）、业务角色（默认 `shinano`）与密码；可选的管理员步骤见第 3 节。
6. **RustFS**：内部地址（如 `http://192.0.2.10:9000`）、IM 可达地址（留空表示与内部地址相同）、AccessKey、SecretKey。
7. **LLBot**：主机、端口（默认 3000）、Milky 路径前缀（没有就留空）、AccessToken（没有就留空）。
8. **面板与状态页的监听地址和端口**：默认 `127.0.0.1`，端口分别为 19100、19101。需要局域网访问时填本机的局域网 IP；不接受 `0.0.0.0`、`::` 这类全部网卡地址。
9. **HTTPS 证书**（可选）：PFX 文件，相对路径基于部署数据目录，可以带密码；状态页可以使用同一张证书。工具不生成自签名证书。证书无法加载时，对应站点只在 `127.0.0.1` 以 HTTP 监听，初始化时会提前告警。
10. **NB2 运行环境**：是否现在创建（需要 uv）。失败不影响其余初始化，之后可以运行 `scripts/nb2-setup` 重试。

结束时工具会：

- 生成 64 位面板登录 Token，写入本地配置，**只在最后显示这一次**，请妥善保存。以后再运行 init，已有的 Token 不会变。忘记 Token 时，可以查看 `data/shinano.local.json` 中的 `Shinano:Web:Token`。
- 首次初始化时把随包插件装入插件目录，全部默认不启用。
- 直接运行模式下询问是否立即启动；服务模式下创建并启动服务（见第 5 节）。

### 非交互

加 `--accept-defaults` 后不再询问：未给出的项取默认值或现有值，缺少必填项时报错退出。普通项用命令行选项给出，敏感值只通过环境变量给出：

| 选项 | 对应项 |
|---|---|
| `--run-mode direct\|service` | 运行方式 |
| `--host-service-name`、`--monitor-service-name` | 服务名 |
| `--service-user yes\|no` | Linux 是否使用专用用户 |
| `--timezone`、`--bucket-prefix`、`--super-users` | 部署配置 |
| `--db-host`、`--db-port`、`--db-name`、`--db-user` | 数据库连接 |
| `--db-admin-user` | 给出时执行管理员步骤 |
| `--rustfs-endpoint`、`--rustfs-im-endpoint` | RustFS 地址 |
| `--llbot-host`、`--llbot-port`、`--llbot-prefix` | LLBot 连接 |
| `--panel-listen`、`--panel-port`、`--status-listen`、`--status-port` | 监听 |
| `--cert-path`、`--status-same-cert yes\|no`、`--status-cert-path` | 证书 |
| `--nb2-setup yes\|no` | 是否创建 NB2 运行环境 |
| `--no-start` | 结束后不询问是否启动 |

| 环境变量 | 敏感值 |
|---|---|
| `SHINANO_INIT_DB_PASSWORD` | 业务角色密码 |
| `SHINANO_INIT_DB_ADMIN_PASSWORD` | 管理员密码 |
| `SHINANO_INIT_RUSTFS_ACCESS_KEY`、`SHINANO_INIT_RUSTFS_SECRET_KEY` | RustFS 密钥 |
| `SHINANO_INIT_LLBOT_TOKEN` | LLBot AccessToken |
| `SHINANO_INIT_CERT_PASSWORD`、`SHINANO_INIT_STATUS_CERT_PASSWORD` | 证书密码 |

选项也可以写成 `--名称=值`。PowerShell 中同样使用 `--名称`。

## 5. 运行方式

### 直接运行

```sh
scripts/start.sh       # 或 scripts/start.ps1
```

尚未初始化时，会先执行初始化。之后由工具在前台守护宿主与状态页：

- 宿主输出到当前控制台；状态页只写自己的日志。
- 面板中执行重启后，宿主以退出码 75 退出，由工具重新拉起。
- 重启带有插件包变更时，新进程要在 60 秒内就绪。超时则结束该进程再启动一次，由宿主还原上一套插件包。
- 按 Ctrl+C 停止。工具会等宿主和状态页正常退出：宿主最多等 60 秒，状态页最多等 30 秒，超时后强制结束。

不经工具、直接运行 `host/Shinano.Host` 时，面板重启只会让进程退出，不会重新拉起。

### 系统服务

初始化时选择 `service`，或之后运行 `scripts/service install`，都会创建宿主和状态页两个服务并启动，然后等待宿主就绪：

- **Windows**：使用虚拟服务账户 `NT SERVICE\<服务名>`，延迟自动启动；失败后自动重启，包括非崩溃的非零退出。
- **Linux**：unit 文件先生成在 `data/service/`，再安装到 `/etc/systemd/system`，设置为 `Restart=on-failure`。选择专用用户时，服务以 `shinano` 用户运行，部署目录归该用户所有。

创建服务需要管理员权限。没有权限时，工具会尝试提权（Windows 经 UAC，Linux 经 sudo）；无法提权或非交互运行时，只输出需要管理员执行的命令，退出码为 2。

日常管理：

```sh
scripts/service.sh start|stop|status
scripts/service.sh install|uninstall
```

服务模式下修改本地配置后，先 `service stop` 再 `service start` 才会生效。改变运行方式或服务名时重新运行 init，工具会先删除原有服务。

## 6. 首次配置

1. **登录面板**：用浏览器打开 `http://<面板监听地址>:19100`（配置了证书时为 https），输入初始化时显示的 Token。登录后的会话保留 7 天，有活动时自动续期；在「宿主配置」中重新生成 Token 后，现有会话全部失效。
2. **检查总览**：总览页的健康应为「正常」；「Bot 状态」页应显示已连接的机器人账号。
3. **启用插件**：在「插件管理」中逐个启用，并调整插件顺序。这个顺序同时决定跨插件的指令优先级和普通事件的传递顺序；建议把 NB2Bridge 放在最后。启停从下一条消息开始生效，不需要重启。
4. **插件配置与机密**：每个插件在侧边栏有自己的页面。配置表单保存后立即生效。机密只能写入，不会回显，页面只显示是否已配置。
   - 搜图、Hso：在机密中填写图源服务令牌。
   - Hso：图库可以在面板的图库页管理，也可以用 `scripts/hso-import --dir <图片目录>` 批量导入；导入前需要先在面板启用过一次 Hso 插件。
5. **B站订阅**：在 B站订阅页扫码登录；也可以由 Super User 私聊 Bot 发送「B站登录」，二维码会私聊发回。动态来自登录账号的关注流，所以要推送的 UP 需要先用这个 B站账号关注。关注列表显示后，按 UP 设置推送动态、直播和目标群，新关注的 UP 默认不推送。动态默认每 10 分钟轮询一次。
6. **NB2Bridge**：需要先有 NB2 运行环境（初始化时创建，或运行 `scripts/nb2-setup`）。启用插件后 NB2 服务随之启动，停用时停止。NB2Bridge 页面可以查看运行状态与日志，管理 NB2 插件（按 PyPI 包名或上传 wheel 安装）以及 NB2 插件的配置。初始环境包含链接解析插件 nonebot-plugin-parser，默认不启用。验证方法：在群里发 `/nb2ping`，应回复 `pong`。
7. **宿主配置**：其他部署配置在「宿主配置」页修改（项目见附录）。数据库、LLBot、RustFS 与证书配置在保存前会用新值实际连接或加载，通过后才写入。保存时会提示是否需要重启，需要时在面板执行重启即可。

## 7. 状态页

状态页由独立进程提供，默认地址 `http://127.0.0.1:19101`，只读，不需要登录。面板侧边栏底部有它的链接。

- 默认每 30 秒探测一次：宿主（`/healthz`）、PostgreSQL（`select 1`）、RustFS（`/health`）、LLBot（`get_login_info`），每项超时 5 秒。
- 页面显示各项当前等级、耗时、24 小时和 7 天可用率，以及最近 24 小时的状态条带。失败只显示类别，不显示地址与凭据。
- 探测记录写在 `monitor/data/probes-<日期>.jsonl`，保留 7 天，不依赖 PostgreSQL。
- 状态页不发 IM 告警，也不负责重启宿主。
- 宿主停止时，状态页仍能显示依赖服务的状态。宿主的 `/healthz` 只接受本机访问。

## 8. 升级与插件包管理

### 升级程序

1. **备份数据库**（如 `pg_dump`）。新版本可能执行数据库结构迁移，迁移不会回退。
2. 把新发布包解压到另一个目录，从新目录运行：

   ```sh
   scripts/upgrade.sh --target <现有部署根目录>
   ```

3. **直接运行模式**要求宿主和状态页已经停止。**服务模式**下，工具取得管理员权限后会停止服务、替换程序、再启动服务，并等待宿主就绪。

升级时，程序目录整体替换。旧版移到 `.previous/`，只保留最近一次。两个进程的 `Logs/` 和状态页的探测记录会保留下来。替换失败时自动还原旧版；替换成功后随包插件同步等步骤失败时，新版本已经装好，工具如实报告失败的步骤，不回退程序。服务模式下无论成败都会重新启动服务。新包中的随包插件在停止服务之前读取，插件包损坏时不做任何改动就退出。

升级不询问配置，新增的配置项取默认值。随包插件的处理：已安装且新包版本更高的，进入待处理变更，由面板重启时应用；新增的直接装入，不启用；管理员删除过的不再装回。

### 插件包

在「插件管理」中上传插件包（zip）、升级或删除插件。这些变更**不会立即生效**：

- 页面会列出全部待处理变更（安装、升级及新旧版本、删除），每一项都可以撤销。
- 在面板执行重启后才应用。重启期间页面会自动恢复连接。
- 带变更的新进程未能就绪时，自动还原上一套插件包，并向 Super User 发送通知（`PLUGIN-ROLLBACK`）。升级允许降级。

删除插件时，可以选择同时清除插件数据。选择后，第二个对话框会列出将要删除的表、bucket、机密文件与浏览器用户目录，需要再次确认。清除在重启成功后于后台执行；失败的步骤下次启动时重试，并通知 Super User 一次（`PLUGIN-PURGE-FAILED`）。

插件只运行管理员认可来源的可信代码，插件拥有 Bot 进程的全部权限。

## 9. 日志、数据与备份

- **宿主日志**：`host/Logs/<日期>-<启动时间>.log`。默认等级 Information，目录总大小超过 2048 MiB 时从最旧的文件开始删除。面板的「日志」页可以查看实时日志和历史文件。
- **状态页日志**：`monitor/Logs/`，上限 256 MiB。
- **工具操作记录**：`data/tool.log`。
- **NB2 日志**：`data/nb2/logs/`，按大小轮转。
- **过期清理**：每次启动时执行一次，之后每小时一次，并在日志中记录删除数量：
  - 聊天记录默认保留 30 天，按天分区删除，对应的聊天媒体一起删除；
  - 无引用的持久媒体过了宽限期（默认 24 小时）后删除；
  - 发言统计默认永久保存。

需要备份的内容：

| 内容 | 位置 |
|---|---|
| 数据库 | PostgreSQL 中的部署数据库（`pg_dump`） |
| 媒体 | RustFS 中以 bucket 前缀开头的 bucket：`<前缀>-chat`、`<前缀>-media`，以及插件 bucket（如 `<前缀>-hso`） |
| 部署数据 | `data/` 整个目录（本地配置、机密、会话密钥、插件、浏览器用户目录、NB2 环境） |

`data/ffmpeg/` 与 `data/browser/` 中的浏览器程序可以重新下载，不备份也可以，但 `data/browser/` 中的插件用户目录保存着 B站登录状态。

## 10. 故障处理

健康分为正常、降级、停止三级，整体状态取最差的一项。宿主发给 Super User 的私聊通知都是卡片图片，左下角是通知种类代号。

| 情况 | 行为 | 通知 |
|---|---|---|
| 启动时数据库或 RustFS 检查失败 | Bot 不上线，面板继续提供故障信息与日志 | — |
| 运行中数据库离线 | 停止：暂停全部插件，直到数据库恢复；期间的消息无法入库，恢复后登记为归档缺口 | `DB-OFFLINE`、`DB-RECOVERED` |
| 运行中 RustFS 离线 | 降级：消息正文照常入库，媒体标记为缺失；需要媒体存储的功能报错 | `RUSTFS-OFFLINE`、`RUSTFS-RECOVERED` |
| LLBot 断线 | 停止：自动重连；断线期间的消息无法补回，登记为归档缺口 | — |
| 归档缓冲已满 | 丢弃新记录，登记为归档积压缺口 | `ARCHIVE-BACKLOG`、`ARCHIVE-RECOVERED` |
| NB2 意外退出 | 自动重启；1 分钟内超过上限（默认 3 次）后停止 | `NB2-STOPPED` |
| B站登录失效、风控 | 暂停动态轮询；风控暂停 1 小时后自动恢复 | `BILI-LOGIN-EXPIRED`、`BILI-RISK`、`BILI-RISK-RECOVERED` |

- 数据库和 RustFS 的恢复检查在收到消息时进行，至少间隔 30 秒；故障开始和恢复时各通知一次。
- Super User 在群聊或私聊发送「健康检查」可以强制检查，Bot 回复一张状态卡片：数据库、RustFS、LLBot 连接、日志目录占用，以及已启用的插件（含初始化失败的插件和原因）。
- NB2 的状态不计入宿主健康，在 NB2Bridge 页面查看，或由 Super User 发送「NB2状态」。
- 归档缺口可以在面板的「归档缺口」页查看。
- 排查时以 `host/Logs/` 中的日志为准，通知卡片只写原因和影响。

## 11. 卸载

- **服务模式**：运行 `scripts/service uninstall`，停止并删除两个服务；工具创建过专用用户时，会询问是否删除（`--delete-user yes|no`）。
- **直接运行模式**：停止进程即可。

卸载不会删除部署目录、数据库和 bucket，工具结束时会列出这些仍保留的资源。确认不再需要后，手动删除部署目录、数据库，以及以 bucket 前缀开头的 bucket。

## 附录：`shinano.local.json` 配置项

本地配置位于 `data/shinano.local.json`，所有键都在 `Shinano` 下。未写出的项使用下表中的默认值。除注明的项外，修改后都需要重启宿主。大部分项可以在面板「宿主配置」页修改，标「敏感」的项在面板只写不回显。

| 键 | 默认值 | 说明 |
|---|---|---|
| `Deployment:TimeZone` | `Asia/Shanghai` | IANA 或 Windows 时区 ID；只影响此后新建的聊天分区 |
| `Deployment:BucketPrefix` | `shinano` | bucket 前缀，每个部署唯一；修改后原有媒体不会迁移 |
| `Deployment:SuperUsers` | `[]` | Super User 的 QQ 号列表 |
| `Database:Dsn` | — | 敏感。`postgres://用户:密码@主机:端口/数据库` 形式的 DSN，或 Npgsql 键值对连接串 |
| `LlBot:Host` / `LlBot:Port` | `127.0.0.1` / `3000` | Milky 协议端地址 |
| `LlBot:Prefix` | 空 | Milky 路径前缀，如 `milky` 对应 `/milky/api/...` |
| `LlBot:AccessToken` | 空 | 敏感 |
| `RustFs:Endpoint` | — | Bot 访问 RustFS 的地址 |
| `RustFs:ImEndpoint` | 同 `Endpoint` | IM 协议端下载媒体用的地址 |
| `RustFs:AccessKey` / `RustFs:SecretKey` | — | 敏感 |
| `RustFs:PresignExpiryMinutes` | `10` | 发给协议端的预签名链接有效期（分钟） |
| `Media:MaxFileSizeMiB` | `100` | 聊天媒体与持久媒体的单文件上限 |
| `Media:OrphanGraceHours` | `24` | 持久媒体失去最后一条引用后到物理删除的宽限期 |
| `ChatArchive:RetentionDays` | `30` | 聊天记录保留天数 |
| `ChatArchive:PartitionDaysAhead` | `3` | 提前创建的分区天数 |
| `ChatArchive:BufferCapacity` | `10000` | 归档缓冲容量（条） |
| `ChatArchive:StatRetentionDays` | `0` | 发言统计保留天数，0 为永久 |
| `Logging:MinimumLevel` | `Information` | `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical` |
| `Logging:MaxTotalSizeMiB` | `2048` | 宿主日志目录上限 |
| `Web:ListenAddress` / `Web:Port` | `127.0.0.1` / `19100` | 面板监听；只接受具体 IP 或 `localhost` |
| `Web:CertificatePath` / `Web:CertificatePassword` | 空 | PFX 证书，相对路径基于部署数据目录；密码为敏感项 |
| `Web:Token` | 初始化生成 | 敏感。面板登录 Token，只能在面板重新生成 |
| `Rendering:Width` | `1200` | 消息图片输出宽度（480–3600 像素） |
| `Plugins:Directory` | `plugins` | 插件目录，相对路径基于部署数据目录 |
| `Browser:VersionMode` | `Latest` | `Latest` 最新稳定版、`Pinned` PuppeteerSharp 固定版本、`Specific` 指定主版本 |
| `Browser:Version` | `0` | `Specific` 时的主版本号 |
| `Browser:ExecutablePath` | 空 | 本机浏览器路径；留空时下载 Chrome for Testing |
| `Ffmpeg:Mode` | `Download` | `Download` 自动下载、`SystemPath` 使用系统 `PATH`、`Directory` 使用指定目录 |
| `Ffmpeg:Directory` | 空 | `Directory` 时含 ffmpeg、ffprobe、ffplay 的目录 |
| `Restart:ServiceName` | `Shinano` | 宿主服务名，由初始化写入 |
| `Restart:KeepBackup` | `true` | 插件包变更应用成功后保留上一套插件包（只保留最近一次） |
| `Monitor:ListenAddress` / `Monitor:Port` | `127.0.0.1` / `19101` | 状态页监听；修改后重启状态页 |
| `Monitor:CertificatePath` / `Monitor:CertificatePassword` | 空 | 状态页证书 |
| `Monitor:IntervalSeconds` | `30` | 探测间隔，最小 5 秒；修改后重启状态页 |
| `Monitor:MaxLogSizeMiB` | `256` | 状态页日志目录上限；修改后重启状态页 |
| `Monitor:ServiceName` | `ShinanoMonitor` | 状态页服务名，由初始化写入 |

插件的配置保存在数据库中，在各插件页面修改。插件的机密保存在 `data/secrets/<插件 ID>.json`；直接编辑这个文件也可以，重启后生效。
