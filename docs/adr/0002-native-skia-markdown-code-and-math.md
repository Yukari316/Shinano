# 文档卡片以原生 SkiaSharp 集成语法高亮与 LaTeX 数学公式

Status: accepted

## Context

宿主 [DocumentRenderer.cs](../../src/Shinano.Host/Rendering/DocumentRenderer.cs) 基于 Markdig 与 SkiaSharp（`4.153.1`）在进程内直接绘制终末地风格文档卡片，无外部进程依赖且单次绘制在毫秒级完成。新增 Gemini 对话插件后，长文回复包含大量多语言代码块与 LaTeX 数学公式，需要升级宿主文档渲染器以支持语法高亮与公式排版。

## Decision

继续在宿主进程内基于 SkiaSharp 扩展 `DocumentRenderer`，而不切换为无头浏览器 HTML/CSS 渲染，也不降级 SkiaSharp 版本：

- **语法高亮**：引入纯托管的 `TextMateSharp` 与 `TextMateSharp.Grammars`，将 TextMate token 作用域映射到参考 `endfield-ui-skill` 的终末地暗色代码块色板，逐段绘制高亮代码。
- **LaTeX 数学公式**：引入不依赖 SkiaSharp 的 `CSharpMath.Rendering` 作为 TeX 解析与排版引擎，在宿主内自写轻量 SkiaSharp 4 画布适配层（实现 `CSharpMath.Rendering.FrontEnd.ICanvas`）；行内与块级公式独立容错，单条公式解析或渲染失败时退回原样文本。

## Considered Options

- **引入 `CSharpMath.SkiaSharp` 官方包**：`CSharpMath.SkiaSharp 0.5.1` 停留在 SkiaSharp 2.80.x，与宿主锁定的 `SkiaSharp 4.153.1` 存在 API 与二进制不兼容；为单个公式库降级全工程 SkiaSharp 不可接受。
- **使用宿主无头浏览器（PuppeteerSharp + KaTeX/Shiki）渲染整张文档卡片**：排版能力最强，但宿主浏览器属于按需外部进程且不计入宿主健康状态，会显著拉长每次回复延迟与内存占用，且会打破现有宿主卡片（通知、列表、文档）统一的 SkiaSharp 排版与字体体系。

## Consequences

- `Shinano.Host` 直接维护 `CSharpMath.Rendering` 到 SkiaSharp 4 的小型画布适配层；未来升级 SkiaSharp 时只需维护该适配层，不依赖上游 `CSharpMath.SkiaSharp` 发布进度。
- 语法高亮与公式渲染全部在进程内离线完成，不依赖 Chrome for Testing 与外网，且所有复用 `IPluginImages.RenderDocument` 的插件与宿主调试指令同步受益。
- 受限于 `CSharpMath` 的 TeX 子集，极少数生僻宏或非标准公式语法会自动回退为原始文本显示，不会导致整张文档卡片渲染失败。
