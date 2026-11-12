# ImageSources 共享库约定

搜图（`image-search`）与 Hso（`hso`）共用的类库，不是插件包（没有 `plugin.json`），随引用它的插件一起构建与打包。通用约定见仓库根目录 `AGENTS.md`，两个插件的业务规则见各自目录的 `AGENTS.md`。

- 图源服务 YukariApiNext 的网关客户端：插件上传图片或按 pid 与页号请求，网关原样转发 SauceNAO、IQDB、trace.moe 与 Pixiv、图站的响应；上游凭据由网关持有，插件只持有网关令牌。结果解析与分级由本库与插件完成，网关不负责。
- SauceNAO 解析与 Pixiv 优先：达到阈值的结果中有指向 Pixiv 作品的（结果本身或来源链接是 Pixiv）时取其中相似度最高的一条（`HitPicker`），搜图与 Hso 补全共用。
- 限定检索库（`SearchDatabases`）：只检索能取到原图的库，请求时经网关的 `db` 参数限定，解析时也只保留这些库的结果。
- R-18 判定：Pixiv 为 `x_restrict > 0` 或 R-18/R-18G 标签，booru 与 Sankaku 为分级 e/explicit，IQDB 标注 Explicit 的按 R-18 处理。
- 「H是不行的」替代图嵌在本库中。
