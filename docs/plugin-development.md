# 插件开发

本文写给要为 Shinano 编写插件的开发者。部署与运维见 [deployment.md](deployment.md)，项目概览见 [README](../README.md)。

本文中的接口都在 `src/Shinano.Framework` 中，带有完整的 XML 文档；以代码为准，本文只说明怎样组合使用。仓库自带的插件（`plugins/`）和测试插件（`tests/SamplePlugins/`）可以直接当示例看。

## 1. 插件模型

- **可信的进程内插件**：插件和宿主在同一进程内运行，拥有 Bot 进程的全部权限，宿主不提供沙箱。只安装来源和代码经管理员认可的插件。
- **独立加载上下文**：每个插件使用独立的 `AssemblyLoadContext`。宿主已有的程序集一律使用宿主的那一份，保证插件和宿主看到同一个类型，包括：
  - `Shinano.Framework`、Sora、Autofac、SqlSugar；
  - SkiaSharp（含 HarfBuzz）、PuppeteerSharp、ASP.NET Core。

  宿主没有的依赖才从插件目录按 `.deps.json` 加载。因此插件不能使用与宿主不同版本的共享库。
- **加载与启用分开**：
  - 宿主启动时加载全部已安装的插件：实例化入口类型、建立插件容器、读取面板声明。
  - 插件默认不启用，由管理员在面板的插件管理页启用。
  - 首次启用时：登记指令，执行 `InitializeAsync`，然后开启。
  - 之后的停用与再启用只切换开关，不再调用 `InitializeAsync`。停用后，宿主跳过该插件的指令、事件订阅、后台任务和专有面板。
  - 插件不实现停用方法。
- **重启后生效**：上传、升级和删除插件包都不会在运行中卸载程序集，要等面板的重启操作在新进程中应用。
- **数据库离线时全部暂停**：数据库离线时，宿主暂停全部插件，恢复后按原状态重新启用。暂停不触发 `OnEnabledChanged`。

## 2. 工程结构与构建输出

最简单的方式，是在本仓库的 `plugins/` 下新建工程。`plugins/Directory.Build.props` 已统一设置：

- 输出到 `artifacts/plugins/<工程名>/`，目录布局就是插件包的布局；
- 启用 `EnableDynamicLoading`；
- 复制 `plugin.json`、`assets/**` 和 `web/**`。

工程文件只需引用 Framework，并且不把它复制到输出目录：

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <!-- 契约与 Sora 由宿主提供，插件包内不携带。 -->
    <ProjectReference Include="../../src/Shinano.Framework/Shinano.Framework.csproj" Private="false" ExcludeAssets="runtime"/>
  </ItemGroup>

</Project>
```

Framework 会把 Sora、Sora.Command、Autofac、SqlSugarCore、SkiaSharp、PuppeteerSharp 和 ASP.NET Core 共享框架传递给插件，不需要另外引用。目标框架为 .NET 10，版本与宿主一致（见根目录 `Directory.Packages.props`）。

插件目录的约定：

```
plugins/Example.Plugin.Hello/
├── Example.Plugin.Hello.csproj
├── plugin.json          # 清单，见第 3 节
├── HelloPlugin.cs       # 入口，见第 4 节
├── HelloCommands.cs     # 指令组
├── assets/              # 可选：素材（字体、图片等），经 IPluginAssets 读取
└── web/                 # 可选：预先构建的专有页面，见第 6 节
```

需要多个插件共用的代码，可以放进没有 `plugin.json` 的类库，例如 `plugins/Shinano.Plugin.ImageSources`。插件以普通 `ProjectReference` 引用它，类库随插件一起复制进插件包。

构建：

```
dotnet build Shinano.slnx
```

开发时，可以把宿主本地配置中的插件目录 `Shinano:Plugins:Directory` 指向 `artifacts/plugins`。它默认为部署数据目录下的 `plugins`，相对路径基于部署数据目录。运行中的宿主会锁住插件程序集，构建前先停止宿主。

## 3. plugin.json

插件包根目录下的清单：

```json
{
  "id": "hello",
  "name": "问候",
  "version": "1.0.0",
  "description": "示例插件",
  "entryAssembly": "Example.Plugin.Hello.dll",
  "bucketPurposes": ["hello-cache"],
  "tables": {
    "global": ["hello_template"],
    "account": ["hello_usage"]
  }
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `id` | 是 | 插件 ID，格式为 `^[a-z][a-z0-9-]{1,62}$`。它同时用作表名前缀、调度器和 bucket 的归属，以及机密文件名；发布后不要改。 |
| `name` | 否 | 面板显示的名称。 |
| `version` | 否 | 插件版本。面板显示、上传升级和随包插件升级判断都只看这个字段，不看程序集版本。 |
| `description` | 否 | 面板显示的说明。 |
| `entryAssembly` | 是 | 入口程序集的文件名，必须直接位于插件目录下。它是唯一登记到 Sora 的程序集，指令只能写在这个程序集里。 |
| `bucketPurposes` | 否 | 插件独立 bucket 的用途名，见第 8 节。用途名在全部插件之间不得重复，上传时会检查冲突；`chat` 和 `media` 由宿主保留。 |
| `tables` | 否 | 插件用到的全部数据表，分为 `global` 与 `account` 两组，写完整表名（不含 schema），见第 7 节。 |

宿主在加载时校验以下各项，任何一项不合格，插件都不加载：

- `id` 的格式；
- `entryAssembly` 是否为文件名；
- 表名的前缀、字符集与长度（不超过 63 个字符），以及是否重复声明。

## 4. 入口与 IPluginInitializationContext

入口程序集必须恰好包含一个实现 `IShinanoPlugin` 的公开非抽象类，并带公开的无参构造函数。三个方法由宿主串行调用：

| 方法 | 调用时机 | 用途 |
|---|---|---|
| `ConfigureServices(ContainerBuilder)` | 加载时一次 | 向插件的 Autofac 容器注册服务 |
| `ConfigurePanel(IPanelRegistration)` | 加载时一次 | 声明配置表单与机密表单，与启停无关 |
| `InitializeAsync(IPluginInitializationContext, CancellationToken)` | 首次启用时一次 | 建表与迁移、订阅事件、登记后台任务、登记专有面板 |

`InitializeAsync` 抛出异常时，插件保持停用，面板显示失败原因，健康状态降级。进程重启之前，不会再次尝试初始化。

插件容器中已经注册了以下服务，可以由构造函数注入：

- `IPluginInitializationContext`；
- `IBotAccessor`；
- `ILogger<T>`；
- 入口程序集中全部带 `[CommandGroup]` 的公开类型，注册为单例。

`IPluginInitializationContext` 提供宿主的全部能力：

| 成员 | 说明 |
|---|---|
| `PluginId` | `plugin.json` 中的 `id` |
| `Events` | 普通事件订阅（第 5 节） |
| `Scheduler` | 后台任务（第 9 节） |
| `Database` | 数据库与结构迁移（第 7 节） |
| `Media` / `Buckets` | 持久媒体与插件独立 bucket（第 8 节） |
| `Bot` | 当前机器人连接：`Api`（未连接时为 null）、`SelfId`、`SuperUsers`，以及 `CallMilkyAsync`（不经 Sora 原样调用 Milky API，经此发送的消息不归档） |
| `Assets` | 读取插件包 `assets/` 中的文件 |
| `Panel` | 专有面板：页面与管理 API（第 6 节） |
| `Fonts` / `Images` | 公共字体与统一风格的消息图片（第 9 节） |
| `Browser` / `Tools` | 无头浏览器与 ffmpeg（第 9 节） |
| `Notices` | 通知全部 Super User（第 9 节） |
| `Config<T>()` / `Secrets<T>()` | 配置与机密（第 6 节） |
| `DataDirectory` | 部署数据目录的绝对路径。只供需要管理外部程序及其数据的插件使用，且只使用约定给自己的子目录。 |
| `OnEnabledChanged(Action<bool>)` | 启停回调。只供必须随启停管理外部进程的插件使用，只能在 `InitializeAsync` 中登记。 |

`Events.Subscribe`、`Scheduler.Register`、`Panel.UsePage` 和 `OnEnabledChanged` 都只能在 `InitializeAsync` 中调用。

在 `InitializeAsync` 中需要使用容器里的服务时，可以在 `ConfigureServices` 里用 `RegisterBuildCallback` 取得容器，这样与指令组共用同一批单例：

```csharp
using Autofac;
using Shinano.Framework.Plugins;
using Sora.Entities.Events;

namespace Example.Plugin.Hello;

/// <summary>问候插件入口。</summary>
public sealed class HelloPlugin : IShinanoPlugin
{
    private static readonly TimeSpan CLEANUP_INTERVAL = TimeSpan.FromHours(1);

    private ILifetimeScope? _container;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.RegisterType<HelloStore>().SingleInstance();
        builder.RegisterBuildCallback(scope => _container = scope);
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel) => panel.UseConfig<HelloConfig>();

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        ILifetimeScope container = _container ?? throw new InvalidOperationException("插件容器尚未建立");
        HelloStore store = container.Resolve<HelloStore>();
        store.Initialize();
        context.Events.Subscribe<MessageReceivedEvent>(store.OnMessageAsync);
        context.Scheduler.Register("hello-cleanup", CLEANUP_INTERVAL, store.CleanupAsync, runImmediately: false);
        return ValueTask.CompletedTask;
    }
}
```

## 5. 指令与事件

### 指令

指令使用 Sora 原生的 `[CommandGroup]` 与 `[Command]`。指令组由插件容器构造，构造函数可以注入插件服务。回复使用事件参数的原生 API，或 Framework 提供的 `e.ReplyAsync(MessageBody)`：它按消息来源回复到群或私聊。插件不需要调用归档接口，宿主的消息记录器会记录全部成功发送的消息。

```csharp
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Message;
using MatchType = Sora.Core.Enums.MatchType;

namespace Example.Plugin.Hello;

/// <summary>问候指令。</summary>
[CommandGroup(Name = "hello")]
public sealed class HelloCommands
{
    private const int COOLDOWN_SECONDS = 10;

    private readonly IPluginConfig<HelloConfig> _config;

    /// <summary>创建指令组。</summary>
    public HelloCommands(IPluginInitializationContext context)
    {
        _config = context.Config<HelloConfig>();
    }

    /// <summary>回复配置中的问候语。</summary>
    [Command(
        Expressions = ["你好"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        Description = "打招呼")]
    [Cooldown(COOLDOWN_SECONDS)]
    public async ValueTask HelloAsync(MessageReceivedEvent e) =>
        await e.ReplyAsync(new MessageBody(_config.Value.Greeting));

    /// <summary>只有群管理员与 Super User 可用。</summary>
    [Command(
        Expressions = ["问候设置"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group,
        Description = "问候设置")]
    [RequirePermission(MemberRole.Admin)]
    public async ValueTask SettingsAsync(MessageReceivedEvent e) =>
        await e.ReplyAsync(new MessageBody("好的！"));
}
```

**权限**：

- Sora 的 `PermissionLevel` 是 IM 成员角色，不会让 Super User 绕过群角色检查。因此 Shinano 的指令在 `[Command]` 上保持 Member 级，用 `[RequirePermission(MemberRole.X)]` 声明要求的角色：
  - Super User 一律放行；
  - 群聊比较成员角色；
  - 私聊没有 IM 角色，要求高于 Member 时，非 Super User 一律拒绝；
  - 拒绝时不回复，但指令已命中，消息被占用。
- 仅限 Super User 的指令，使用 Sora 原生的 `SuperUserOnly = true`。
- Super User 列表来自部署配置，可以从 `IBotAccessor.SuperUsers` 读取。

**冷却**：`[Cooldown(秒数)]` 按群与发送者计时：

- 标在方法上，单条指令冷却；标在类上，整组共享冷却。
- 设置了 `Reply` 时，冷却中回复该文字。
- 它排在 `RequirePermission` 之后，被拒绝的调用不计入冷却。
- 耗时较长、要在完成后才开始计时的指令，可以改用 `CooldownTracker` 的 `IsCooling` 与 `Record`。

**占用消息**：

- Sora 的指令默认 `BlockAfterMatch = true`：匹配后，消息不再交给后续指令与普通事件。
- 需要在处理器里再决定是否占用时，设 `BlockAfterMatch = false`，并在确认处理时设 `e.IsContinueEventChain = false`。`plugins/Shinano.Plugin.Cheru` 就是这样做的。

### 普通事件

```csharp
context.Events.Subscribe<MessageReceivedEvent>(async e =>
{
    if (e.Message.Body.GetText() != "hello") return;
    await e.ReplyAsync(new MessageBody("world"));
    e.IsContinueEventChain = false;
});
```

- `Subscribe<TEvent>` 订阅该事件及其派生事件。处理器抛出的异常只记录日志。
- Sora 先处理全部指令，再分发普通事件；已被指令占用的消息不会到达订阅者。
- 管理页上只有一个插件顺序。它同时决定跨插件的指令优先级（Sora 程序集优先级）和普通事件订阅链的顺序；指令自身的优先级只在插件内部生效。
- 某个处理器把 `IsContinueEventChain` 设为 false 后，后续订阅者不再执行，包括排在最后的 NB2Bridge。
- 插件的启停从下一条消息开始生效。正在分发的消息，以及正在等待连续对话的处理器，都视为已开始的处理：停用后宿主不会取消它们，由其自身的超时结束。

## 6. 配置、机密与面板

面板有两类，插件可以只提供其中一种，也可以两种都提供。

### 配置表单

在 `ConfigurePanel` 中调用 `panel.UseConfig<T>()`，宿主会按类型的 JSON Schema 生成通用表单：

- 插件加载后即可修改，与是否启用无关；
- 配置存于数据库，切换机器人账号后保留；
- 无参构造的结果就是默认配置；
- 插件以 `context.Config<T>().Value` 读取，面板保存后返回新实例。

```csharp
using System.ComponentModel.DataAnnotations;

namespace Example.Plugin.Hello;

/// <summary>问候插件配置。</summary>
public sealed class HelloConfig
{
    /// <summary>问候语。</summary>
    [Display(Name = "问候语", Description = "「你好」指令回复的内容", Order = 1)]
    [Required]
    [StringLength(50)]
    public string Greeting { get; set; } = "你好喵！";

    /// <summary>每日上限。</summary>
    [Display(Name = "每日上限", Order = 2)]
    [Range(1, 100)]
    public int DailyLimit { get; set; } = 10;
}
```

- `[Display]` 的 `Name`、`Description` 和 `Order` 分别作为字段的标题、说明和顺序。
- `[Range]`、`[StringLength]`、`[RegularExpression]` 和 `[Required]` 既用于前端提示，保存时宿主也会再校验一次。宿主只校验配置对象的顶层。
- 每个插件只能使用一个配置类型，且与 `UseConfig` 声明的类型相同。

没有可配置项的插件不声明配置类型，插件页显示「无可配置项」。

### 机密

密钥和登录状态这类内容，用 `panel.UseSecrets<T>()` 声明，用 `context.Secrets<T>()` 读写：

- 存放位置：部署数据目录的 `secrets/<插件 ID>.json`，不进数据库，面板不回显已保存的值；
- 字符串属性在面板中只写：输入新值即替换，也可以清除，保存后 `Value` 立即返回新值；
- 其他类型的属性（如插件写入的登录状态）只显示「已配置/未配置」，由插件用 `SaveAsync` 整体写回；
- 管理员也可以直接编辑这个文件，重启后生效。

插件不要把机密写进日志。

### 专有页面与管理 API

只有存在复杂业务逻辑时，才需要编写专有页面。在 `InitializeAsync` 中登记：

```csharp
context.Panel.UsePage("问候", "index.js");
context.Panel.MapApi(api => api.MapGet("/stats", () => Results.Ok(new { Count = 0 })));
```

`MapApi` 需要 `using Microsoft.AspNetCore.Builder;` 与 `using Microsoft.AspNetCore.Http;`。

- **生效范围**：专有页面在插件启用时加入面板侧边栏，停用时移除；停用期间，页面与 API 都返回 404。
- **API**：`MapApi` 收到的路由前缀是 `/api/plugin-api/{插件 ID}`，已要求面板登录，可以多次调用。
- **页面入口**：`UsePage` 的入口是相对插件包 `web/` 目录的路径。宿主通过 `/plugin-web/{插件 ID}/{入口}` 动态 `import` 这个模块。
- **页面模块**：默认导出一个 Vue 组件，宿主以 `context` 属性传入 `PluginPageContext`（来自 `@shinano/plugin-sdk`）。它包含：
  - `pluginId`；
  - `api(path, init)`：`path` 相对插件 API 前缀，以 `/` 开头；请求携带面板会话，会话失效时宿主跳转登录页。
- **共享依赖**：`vue` 与 `@shinano/plugin-sdk` 由面板的 import map 提供，构建时必须声明为外部依赖，不能自带 Vue 运行时。其他依赖（如图标库）打进插件的模块。
- **样式**：页面不加载单独的 CSS 文件。面板的 Tailwind 与 daisyUI 样式在构建面板时，从本仓库 `web/plugins/` 的源码一并生成。因此仓库外的插件只能使用面板已经生成的类，或使用行内样式。
- **示例**：自带插件的页面源码在 `web/plugins/<名称>/`，是 pnpm 工作区包，用 Vue 单文件组件与 TypeScript 编写，用 Vite 以库模式构建为单个 ES 模块，输出到插件工程的 `web/`。`web/plugins/qa/vite.config.ts` 可以作为模板：

```ts
export default defineConfig({
  plugins: [vue()],
  build: {
    outDir: fileURLToPath(new URL('../../../plugins/Example.Plugin.Hello/web', import.meta.url)),
    emptyOutDir: true,
    lib: { entry: 'src/index.ts', formats: ['es'], fileName: () => 'index.js' },
    rollupOptions: { external: ['vue', '@shinano/plugin-sdk'] }
  }
})
```

```vue
<script setup lang="ts">
import type { PluginPageContext } from '@shinano/plugin-sdk'
import { onMounted, ref } from 'vue'

const props = defineProps<{ context: PluginPageContext }>()
const count = ref(0)

onMounted(async () => {
  const response = await props.context.api('/stats')
  if (response.ok) count.value = ((await response.json()) as { count: number }).count
})
</script>

<template>
  <div class="card bg-base-100"><div class="card-body">今日问候 {{ count }} 次</div></div>
</template>
```

面板主题为 daisyUI 的 `emerald`（亮色）与 `dim`（暗色）。页面使用 daisyUI 的语义色，不要硬编码颜色。

## 7. 数据库

数据库为 PostgreSQL，经 SqlSugar 访问。`context.Database` 提供：

- `Global`：全局 schema `global` 的客户端，放与账号无关的数据；
- `Account`：当前机器人账号 schema `user_<self_id>` 的客户端，放属于账号的数据；
- `SelfId`：当前机器人账号。

两个客户端都是线程安全的。一个进程只连接一个账号；切换账号后，`Account` 指向新账号的 schema，旧账号的数据保留。

**表名**：

- 以插件前缀开头：插件 ID 中的 `-` 换成 `_` 后，再加 `_`。例如 `image-search` 的前缀为 `image_search_`。
- 只含小写字母、数字与 `_`，不超过 63 个字符。
- 每一张表都要按 schema 写进 `plugin.json` 的 `tables`。管理员清除插件数据时，宿主只删除这些表（按表名全字匹配）。以插件前缀开头但没有声明的表，只会列为「可能的残留」，不会删除。

**建表**：表只由带 `[SugarTable]` 的实体经 CodeFirst 创建，在 `InitializeAsync` 中执行：

- 不手写建表语句；
- 不使用动态表名或分表；
- `InitTables` 会创建缺失的表，并补齐新增的可空列。

```csharp
using SqlSugar;

namespace Example.Plugin.Hello.Entities;

/// <summary>每个群的问候次数，位于账号 schema。</summary>
[SugarTable("hello_usage")]
[SugarIndex("ux_hello_usage_group", nameof(GroupId), OrderByType.Asc, true)]
public sealed class HelloUsageRecord
{
    /// <summary>行 ID。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>群号。</summary>
    [SugarColumn(ColumnName = "group_id")]
    public long GroupId { get; set; }

    /// <summary>最后一次问候的时间。</summary>
    [SugarColumn(ColumnName = "last_used_at")]
    public DateTimeOffset LastUsedAt { get; set; }

    /// <summary>次数。</summary>
    [SugarColumn(ColumnName = "count")]
    public int Count { get; set; }
}
```

```csharp
context.Database.Account.CodeFirst.InitTables<HelloUsageRecord>();
```

**破坏性变更**：删列、改类型、改名等变更，要提供带版本号的显式迁移，在 `InitializeAsync` 中执行：

```csharp
await context.Database.MigrateAccountAsync(
    [
        new PluginMigration
        {
            Version = 1,
            Apply   = async (db, ct) => await db.Ado.ExecuteCommandAsync("alter table hello_usage drop column if exists legacy_note")
        }
    ],
    cancellationToken);
```

- 版本在同一插件、同一 schema 内从 1 开始严格递增。宿主在版本表中记录已经执行的版本，只执行新的版本。
- 每个迁移在事务中执行，`search_path` 已指向目标 schema。
- 迁移失败时抛出异常，插件保持停用。
- 全局 schema 的迁移使用 `MigrateGlobalAsync`。

数据读写优先使用 SqlSugar 的实体 API。手写 SQL 时，值一律参数化。

## 8. 媒体与 bucket

RustFS 经 S3 协议访问，bucket 名为 `<部署前缀>-<用途>`。插件有两种存储方式。

**持久媒体 `context.Media`**：

- 由宿主统一管理，按内容去重，适合「来自聊天、需要长期保存」的图片和文件。
- `StoreAsync` / `StoreFromUrlAsync` 保存媒体，并以业务键 `refKey` 登记引用，返回媒体 ID；`AddReferenceAsync` 为已有媒体再登记一个引用。
- `selfId` 写业务对象所属的账号，与账号无关时传 null。
- 插件只增删自己的引用（`RemoveReferencesAsync`），不能删除对象。最后一条引用删除后，经宽限期（默认 24 小时）由宿主回收。
- 发给 IM 时，用 `CreateImUrlAsync` 生成预签名临时链接。
- 超过部署配置的单文件上限时抛出异常。

**插件独立 bucket `context.Buckets.Get(用途)`**：

- 适合对象不来自聊天、由插件自己管理生命周期的数据，例如图库。
- 用途必须在 `plugin.json` 的 `bucketPurposes` 中声明，否则抛出 `ArgumentException`。用途名只含小写字母、数字与连字符，拼接后的 bucket 名不超过 63 个字符。
- `IPluginBucket` 提供：
  - `EnsureCreatedAsync` / `DeleteAsync`：创建与删除 bucket；
  - `PutAsync`：写入，上限取 `limitBytes` 与部署上限中较小的一个；
  - `OpenReadAsync`、`GetInfoAsync`、`ListAsync`、`DeleteObjectAsync`：读取、元数据、列出与删除对象；
  - `CreateImUrlAsync`：生成 IM 可下载的临时链接。

  插件只能访问自己声明的 bucket。

插件被清除数据时，宿主会删除：

- 插件的持久媒体引用；
- 声明的全部 bucket；
- 声明的表与结构版本记录；
- 配置与启用状态；
- 机密文件；
- 浏览器用户目录。

运行中 RustFS 离线时，宿主不停止插件。需要媒体存储的功能，应当捕获异常并直接报错。

## 9. 宿主提供的其他能力

- **后台任务** `Scheduler.Register(name, interval, job, runImmediately)`：
  - 插件不得自建定时器；
  - 停用后宿主不再触发，正在执行的一次允许完成；
  - 上一次尚未结束时，跳过本次触发；
  - 传入的取消令牌只在进程停止时取消；
  - 任务异常只记录日志。
- **主动发送**：没有事件参数的场景（如后台任务），使用 `context.Bot.Api`。未连接 LLBot 时为 null，要先判断。
- **Super User 通知** `Notices.NotifySuperUsers(SuperUserNotice)`：
  - 在后台以宿主通知卡片私聊全部 Super User，不等待结果，也不重试；
  - 正文只有原因（`Cause`）与影响（`Impact`）两行，`Code` 是显示在页脚的英文代号（如 `HELLO-QUOTA`）；
  - 同一故障只通知一次，由插件自己去重。
- **消息图片** `Images`：宿主统一风格的 PNG 图片，以 `base64://` 加 Base64 内容作为图片地址发送，不依赖 RustFS：
  - 通知 `RenderNotice`；
  - 列表 `RenderList`；
  - 排行 `RenderRanking`；
  - 信息卡 `RenderInfo`；
  - 状态报告 `RenderStatus`；
  - Markdown 文档 `RenderDocument`；
  - 只画外框、内容由插件绘制的框架卡片 `RenderFramed`。

  卡片模型在 `Shinano.Framework.Rendering.Models`，色板为 `CardPalette`。宿主字体缺失时抛出 `InvalidOperationException`，调用方应改发文字。

  ```csharp
  private const string BASE64_PREFIX = "base64://";

  byte[] png = _context.Images.RenderInfo(
      new InfoCard
      {
          Label = "HELLO",
          Title = "今日问候",
          Rows  = [new CardRow { Label = "次数", Text = "3" }]
      });
  await e.ReplyAsync(new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png)));
  ```
- **字体** `Fonts`：
  - 公共字体：Noto Sans SC 常规与粗体、Space Grotesk、JetBrains Mono、Twemoji 彩色 emoji。
  - 文字绘制辅助 `Painter` 带字体回退与 HarfBuzz 整形。
  - 字体由宿主持有，插件不得释放。
  - 公共字体不够用时，插件把字体放进自己的 `assets/`，用 `Assets.ReadAllBytesAsync` 读取。
- **浏览器** `Browser.OpenAsync`：
  - 供需要真实浏览器维护登录或风控状态的插件使用，打开一个无头 Chrome 会话（PuppeteerSharp 的 `IPage`）。
  - 每个插件有独立的持久用户目录，视同机密。
  - 同一插件的会话串行执行；使用完必须释放，以关闭浏览器。
  - `Identity` 给出浏览器对外的 UA 与 Client Hints，插件自己的 HTTP 请求应使用同一组请求头。
  - 浏览器不计入宿主健康状态，不可用时抛出 `InvalidOperationException`。
- **ffmpeg** `Tools.FfmpegDirectoryAsync`：
  - 返回含 ffmpeg、ffprobe、ffplay 的目录。
  - 首次调用时解析；自动下载模式下可能要下载约 200 MB。
  - 失败不缓存，下次调用重新解析；不可用时抛出 `InvalidOperationException`。

**文案约定**：Bot 在聊天中的回复写给群友看，不写错误码与技术细节，这些写进日志。面板、日志和通知卡片面向管理员，保持清楚直白。

## 10. 打包、安装与升级

**插件包**：

- 插件包是单个 zip，根目录是插件的构建输出：`plugin.json`、入口程序集及其私有依赖，以及可选的 `web/` 和 `assets/`。
- 宿主提供的程序集和原生库不要放进插件包。`build/pack.ps1` 打包随包插件时，会删除输出中的 `runtimes/` 目录。
- 上传限制：压缩包不超过 100 MiB，解压后不超过 512 MiB，条目不能越出插件目录。

**安装**：

- 在面板的插件管理页上传插件包。宿主校验 `plugin.json`、入口程序集和 bucket 用途冲突后暂存，并列入待处理变更：新插件为安装，已安装的为升级。
- 升级不比较版本，允许降级。
- 待处理变更只由面板的重启操作应用，重启前可以逐项撤销。

**回滚**：在 `Shinano.Tool run` 或服务管理器托管的部署中，有两种情况会还原上一套插件包：

- 新进程 60 秒内没有就绪，或本次变更的插件包加载失败；
- 插件初始化失败不算在内，只会让该插件保持停用。

**删除**：

- 删除插件包时，可以选择同时清除插件数据，范围见第 8 节。
- 选择清除需要二次确认，清除在重启确认成功后于后台执行。

**随包插件**：本仓库 `plugins/` 下带 `plugin.json` 的工程，由 `build/pack.ps1` / `pack.sh` 打成随包插件。详见 [deployment.md](deployment.md)。

## 11. 示例

- `tests/SamplePlugins/Shinano.Tests.SamplePluginA`：
  - 指令组构造函数注入插件服务；
  - `RequirePermission` 与 `Cooldown`；
  - 配置表单（含校验特性与枚举）；
  - 机密（字符串项与只显示状态的项）；
  - 终止事件链的事件订阅。
- `tests/SamplePlugins/Shinano.Tests.SamplePluginB`：
  - 只有专有面板：`UsePage` 加上 `MapApi`；
  - 手写的 `web/index.js` 页面；
  - 不终止事件链的订阅。
- `plugins/` 下的自带插件：
  - 在线检测、娱乐、切噜：最小的指令插件；
  - 有人问：账号 schema 的表、持久媒体、专有页面；
  - Hso：插件独立 bucket、后台任务、审核页面；
  - B站订阅：浏览器服务、机密、框架卡片；
  - NB2Bridge：启停回调、ffmpeg、`CallMilkyAsync`。

  每个插件目录下的 `AGENTS.md` 记录了它的业务规则。
