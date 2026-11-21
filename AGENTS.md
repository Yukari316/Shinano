# Shinano 项目约定

> 为避免规则文件超过 Agent 单文件上下文截断上限（24 KB），本文件仅保留全仓通用约束与高频硬规则，各子系统完整规格按目录拆分；**修改对应模块前必须先读该目录下的 `AGENTS.md`**。

## 优先级与证据

- 正确性与真实性 > 安全性与可恢复性 > 可靠性 > 可维护性与架构一致性 > 性能与资源效率 > 交付完整性 > 实现速度与改动规模。
- 以当前代码、测试、日志和真实运行结果为依据，明确区分事实、假设、推断与未验证项。不得把历史笔记、计划或局部检查当作当前实现证据。
- 优先修复根因；发现需求或设计存在冲突时，说明证据、替代方案与权衡，再由用户决定。

## 项目地图与子目录规范索引

- Shinano 是基于 Sora（Milky 协议）的 QQ 机器人，.NET 10，同一进程内托管 Bot、管理 API 与管理面板；另有独立的监控进程与运维工具。
- **按需阅读子目录 `AGENTS.md`**（修改对应区域前必读）：
  - `plugins/<插件名>/AGENTS.md`：各插件独立业务规则（本文件不写单个插件业务逻辑）。
  - `src/Shinano.Host/AGENTS.md`：宿主插件生命周期、使能与权限、重启回滚与数据清除、公共字体与终末地卡片、无头浏览器与 ffmpeg 基础服务、健康检查指令与 Super User 通知卡片。
  - `src/Shinano.Infrastructure/AGENTS.md`：PostgreSQL 运维/离线恢复/初始化权限、RustFS 三类媒体生命周期与预签名直链、聊天记录分区归档与发言统计、日志轮转。
  - `src/Shinano.Tool/AGENTS.md`：跨平台发布包布局、交互式初始化脚本、升级流程与随包插件重启计划、系统服务管理。
  - `src/Shinano.Monitor/AGENTS.md`：独立监控进程、状态页探测与本地 7 天记录。
  - `web/AGENTS.md`：Web 管理面板监听/HTTPS、Token/Cookie 会话、敏感配置、实时日志侧栏、外观与局部滚动布局。
- 其余目录：`src/Shinano.Framework`（插件契约，宿主与插件共享）、`tests/`（测试与测试用插件）、`build/`（发布打包）、`scripts/`（运维脚本入口）。
- 术语见根目录 `GLOSSARY.md`；部署与插件开发说明见 `README.md` 与 `docs/`；开发环境服务地址、账号与凭据文件位置见 `agent_tmp/docs/environment.md`（不入库）。
- `agent_tmp/`（不入库）工作文件分层：`docs/`（进行中的计划、调查与环境说明）、`adr/`（架构决策记录）、`archive/`（已完成或不再需要的文档，阶段结束时移入）、根目录仅放凭据等非文档文件；附属脚本与证据跟随所属文档存放。
- Sora 源码仓库在 `../Sora`（在 Shinano 任务中仅供只读查阅）；旧项目 `../Skadi` 只用于理解旧行为。
- 工作在多台电脑间切换，文档与配置示例中的本机路径一律使用相对本仓库根目录的路径，不写绝对路径。

## 构建、测试与运行

- 构建：`dotnet build Shinano.slnx`。插件输出到 `artifacts/plugins/<工程名>/`（插件包目录布局），测试用插件输出到 `artifacts/test-plugins/`。
- 构建打开了 `EnforceCodeStyleInBuild`，`.editorconfig` 中级别为 warning 的代码风格与命名规则在命令行构建时同样报告，可空性警告直接作为错误。交付前构建须 0 警告 0 错误，警告按待修复问题处理，不靠抑制消除；增量构建可能跳过未改动的工程，核对全部警告时用 `--no-incremental`。IDE 自身的检查（如 Rider 的代码检查）不经过构建，命令行看不到。
- 前端：`pnpm -C web install` 后按包构建（如 `pnpm -C web --filter @shinano/panel run build`）；面板构建产物由宿主托管，插件页面构建到对应插件的 `web/`。
- 测试：`dotnet test --project tests/Shinano.Tests`，聚焦时加 `--filter-class <完整类名>`。集成测试需要 `SHINANO_TEST_DSN` 与 `SHINANO_TEST_S3_ENDPOINT`、`SHINANO_TEST_S3_ACCESS_KEY`、`SHINANO_TEST_S3_SECRET_KEY`，未设置时跳过；调用真实图源服务的测试需要 `SHINANO_TEST_GATEWAY_TOKEN`，浏览器测试需要 `SHINANO_TEST_BROWSER_DIR`，NB2 依赖变更演练（用 uv 从 PyPI 下载）需要 `SHINANO_TEST_NB2_DIR`，只在修改相关功能时手动执行。
- 发布包：`build/pack.ps1` 或 `build/pack.sh`，输出到 `artifacts/release/`。版本号取根目录 `Directory.Build.props` 的 `Version`；插件版本以各自的 `plugin.json` 为准，插件程序集版本在构建时取自同一字段（`plugins/Directory.Build.props`）。
- 运行中的宿主会锁住插件与宿主的程序集，构建前先停止宿主。

## 范围、协作与交付

- 分析、设计、评审和诊断请求默认不修改产品代码；实现、修复和重构请求完成必要修改与验证。
- 只做当前任务必需的改动，不顺手重构、升级依赖、格式化或修复无关问题。必须扩大范围时先说明原因并征得确认。
- 简单局部任务直接完成；清晰且可独立验证的调查或工作委派子代理。主代理负责跨文件边界、决策和最终核验。委派须明确目标、范围、约束、共享文件避让、验证方式与完成条件；独立工作并行，共享文件由主代理收口。
- 用户撤销的方案应完整清理相关代码、测试、注释、文档和命名痕迹。最终文档只描述当前有效行为与有价值的约束。
- 完成报告包含实际修改、验证结果、未验证边界、已知风险、回滚方式以及是否达标；不得把部分完成包装为完成。
- 交付前核对最终 diff，每项修改都必须对应当前需求。未经要求不提交或推送。
- 设计变更、框架接口的新增或职责调整，先提出具体设计与需要用户决定的问题，确认后再实现；不把建议写成已确认的要求。
- git 写操作（提交、推送等）按用户全局规则执行；在适合提交时给出建议范围与提交说明。

## 简单性、验证与注释

- 从当前真实约束选择最短正确路径，不为假设中的需求增加抽象、配置、兼容层或扩展点。
- 不在新系统运行路径保留旧格式兼容、双读双写或遗留分支。一次性迁移的输入、转换和冲突规则须单独确认，不以迁移为由污染新运行契约。
- 只在 HTTP、任务负载、存储和第三方 I/O 等不可信边界验证输入，不重复检查内部类型已保证的不变量。
- 测试聚焦核心路径和有实际风险的契约，不追求穷举或添加只服务测试桩的生产代码。删除重复实现、不可达分支和无行为价值的测试。
- 调试说明根因及发生机制、支持证据、其他合理假设、修复方式与验证结果，并检查相邻同类缺陷。
- 静态检查、聚焦测试、全量测试、真实运行与集成/e2e 是不同等级的证据，必须分别报告。
- 不反复审查已验证且未发生变化的部分；上下文恢复后只核对当前进度与待办。

## 公开仓库与敏感信息

- 仓库在 GitHub 公开。密码、管理 Token、OSS 密钥与完整含凭据的 DSN 不得硬编码或写入可提交示例、前端产物、日志和公开文档，实际值通过部署配置或初始化参数提供。
- 可提交的文件（代码、注释、测试、文档、提交说明）不写真实的 QQ 号、群号、内网地址与本机绝对路径；示例与测试使用虚构值（地址用 `192.0.2.0/24` 等文档专用地址）。真实值只放在 `agent_tmp/docs/environment.md` 与部署配置中。
- 测试使用的图片等样例放在代码库内，不读取仓库外的文件；R-18 等不宜公开展示的图片每个字节与固定值异或后存为 `.bin`（`.gitattributes` 按二进制处理），测试读取时还原，元数据断言使用虚构值。

## C# 仓库特定规范

通用 C# 格式、显式类型、命名、对象初始化、模式匹配、可空性、并发（`System.Threading.Lock`）与注释原则见 **`csharp-coding-standards` skill**；以下为 Shinano 仓库特定约定：

- 文本文件统一使用 UTF-8（无 BOM）+ LF 换行、文件末尾换行。
- 目录只按**功能**划分，不按类型角色划分：不建 `Models/`、`Entities/`、`Endpoints/` 之类的角色目录，同一功能的服务、数据类型、实体与端点放在同一功能目录。层级为工程、功能、子功能，最多两层。工程根目录只放入口 `Program.cs` 与全局 using，`Program.cs` 只做服务登记与启动组合，不定义方法、局部函数或类型。插件工程以根目录为插件本身的功能目录，放入口 `*Plugin`、`*Config`、`*Secrets`、`ReplyTexts` 与少量跨子功能共用类型。
- 子功能是否单独成目录按文件数判断：功能目录超过约 12 个文件且能分出内聚、至少 3 个文件的子功能（如 `Panel/Auth`、`Plugins/Capabilities`、插件的 `Feed`、`Panel`）才拆出子功能目录；总共约 10 个文件以内的小工程保持扁平。
- 持久化自成子功能：SqlSugar 表实体（`*Record`）与读写它的 Store 放在同一目录；一个插件或功能有多个 Store 或多个实体时统一放入 `Persistence/`，业务服务通过 Store 访问数据。
- 用类型名后缀区分同一目录中的不同角色：`Record`（表实体）、`Store`（持久化）、`Options`/`Config`/`Secrets`（配置）、`Endpoints`（HTTP 处理方法）、`PanelApi`（面板业务）、`View`/`Request`/`Update`（面板 DTO）、`Result`/`Outcome`（返回值）、`Kind`/`State`/`Status`（枚举）。同一接口的请求、响应与视图类型合为一个分组文件 `<接口名>Models.cs`（如 `BiliPanelModels.cs`）；被同一功能内多个文件使用的数据类型单独成文件。
- 目录名不得被 `.gitignore` 的通用规则匹配（如 `packages/`、`data/`、`Logs/`，Windows 下不区分大小写），否则源文件不会入库。
- HTTP 路由集中登记：每个 Web 工程只在一个路由登记类中定义全部路径、HTTP 方法与端点元数据（宿主为 `Web/ApiRoutes`，监控为 `Web/MonitorRoutes`）；`*Endpoints` 类只放处理方法，不出现路径字符串也不登记路由；其他代码引用路由登记类常量。插件专有 API 由插件经 `IPluginPanel.MapApi` 在宿主规定的前缀下自行登记。
- XML 文档和解释性注释使用**简体中文**，标识符保持英文。修改已有逻辑段落时将相关英文理由注释同步译为中文，不做无关的全局翻译。
- Autofac 注册默认使用应用级单例，除非生命周期或所有权明确要求其他作用域；不把数据记录或临时协议负载注册为服务。
- 终止或启动外部进程、卸载模块、写入外部进程状态，以及修改系统服务或计划任务配置时，记录请求、结果和可操作的失败信息，不泄露敏感数据。

## 全仓高频硬规则

### 技术栈、插件与权限
- ASP.NET Core + .NET 10 同进程托管 Bot、管理 API 与前端（Autofac 管理依赖），支持 Windows 和 Linux。Sora 通过 NuGet 引用 `HoshikawaKaguya.Sora` 与 `HoshikawaKaguya.Sora.Adapter.Milky`（基线 3.2.0，升级前核实发布版本并由用户确认），不引用本地 Sora 开发工程；Milky 协议单进程单账号（`self_id`），开发端不报告自身消息。
- 主工程不内置业务功能；业务划分为 9 个插件（`Qa`、`Bilibili`、`ImageSearch`、`Hso`、`Entertainment`、`Cheru`、`OnlineCheck`、`NB2Bridge`、`Gemini`）及共享库 `ImageSources`。插件默认不启用，使能由宿主经 Sora 程序集登记统一控制，处理器内不重复判断使能标志；后台任务通过宿主调度器登记，插件不得自建不受管理的定时器。
- Super User 权限不启用已停用插件、不绕过 IM 角色限制：插件指令在 Sora 声明为 Member 级，用宿主 `[RequirePermission(MemberRole)]` 先放行 Super User 再在群聊检 IM 角色（私聊要求高于 Member 时非 Super User 一律拒绝且不回复）；仅限 Super User 的指令用 Sora 原生 `SuperUserOnly`。

### PostgreSQL 与 `SQL_NEED:`
- 使用 PostgreSQL 与 SqlSugar，数据读写优先使用 SqlSugar 实体 API（`Queryable`、`Insertable`、`Updateable`、`Deleteable`、`CodeFirst`）；**只有 SqlSugar 无法表达或无法保证正确性时才手写 SQL（如分区与目录 DDL、`on conflict`、`for update skip locked`、`returning`、原生字符串 `Where` 条件），且必须在该语句或 SQL 常量前添加以 `// SQL_NEED:` 开头的注释说明原因**。手写 SQL 中的值一律参数化，只有经校验的标识符与 DDL 中不能参数化的强类型值可拼入语句。
- 全局数据放 `global` schema，机器人账号数据放 `user_<self_id>` schema，不得混淆机器人账号与群成员身份。
- 插件表名以 `<plugin_id>_` 为前缀（ID 中 `-` 换 `_`，仅小写字母、数字与 `_`，≤63 字符），在 `plugin.json` 的 `tables` 按 `global`/`account` 写出全部表名；原生插件只经 `[SugarTable]` + CodeFirst 建表与补可空列，破坏性变更用带版本号的显式迁移。
- 开发与测试只使用 `shinano_debug`（集成测试用按运行生成的临时 schema 并清理），不创建角色、数据库或扩展，不清理他人数据。

### RustFS 与媒体
- 启动检查可用性，运行中离线不停止全部功能（仅降级告警）。开发与测试只使用 `shinano-debug` 前缀与 `shinano-debug-test-*` 临时 bucket 并清理；不开放 bucket 公共读，IM 端发送媒体使用预签名 GET 直链。

### 聊天回复文案
- Bot 在群聊与私聊中的回复写给群友看，口吻像一个嘴有点欠、带点猫娘腔的群友，而不是系统提示：短句口语，一句话说完，常以感叹号或「喵」收尾；不写技术细节、错误码与操作说明，这些写进日志。
- 用户操作有误时轻轻吐槽对方，不复述规则（「你看看哪还有第a页，只有n页！」「连图都没有真是太逊了」「叽里咕噜说啥呢」）；Bot 或外部服务出问题时拟人化地甩锅给服务器或说自己「爆炸」（「搜图服务器爆炸了喵」「图被夹了，你找服务器要去」）；成功时简短确认（「我记住了！」）。
- 色图相关功能沿用群聊梗，「冲」指找色图，限额、冷却、未配置、无结果都用这个梗表达（「冲不出来喵」「冲的太快了」「你已经冲的太多了，牛牛爆炸了喵」「没有这种色图喵」）；其他功能不硬套这些梗。
- 「喵」只作点缀，不必句句都加；同一功能内的口吻保持一致。
- **新增文案按此风格拟定后列给用户确认，不擅自定稿**。面板、日志、Super User 通知卡片等面向管理员的文字保持清楚直白，不使用此风格。

### Web 管理面板与健康分级
- 使用 pnpm、Vue（单文件组件 + TypeScript）、Tailwind CSS、daisyUI、Phosphor 图标；插件前端通过 import map 共享宿主的 Vue 与 `plugin-sdk`。
- **亮色主题为 daisyUI `emerald`，暗色主题为 `dim`；颜色严格遵循 daisyUI 语义色与颜色变量，禁止自行硬编码配色或创造另一套视觉风格**。
- 敏感配置与插件机密只存于本地文件，Web 只写不回显，接口与日志不返回、不记录其值。
- 服务健康分正常（`success`）、降级（`warning`）、停止（`error`）三级，管理面板、健康检查与监控状态页统一使用；整体状态取各项最差一级。
