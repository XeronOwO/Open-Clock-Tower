# references/ — 外部资料与本机缓存

本目录放**游戏规则的原始出处**。它回答一个问题：**这条规则是从哪儿查来的？**

| 路径 | 内容 | 提交进仓库 |
|---|---|---|
| `wiki-index.json` | 百科抓取索引：页名 + 抓取日期 + 字节数 + SHA256（88 页） | **是** |
| `wiki-all-pages-2026-10-01.txt` | 百科全站页名清单（431 页，2026-10-01 列举），用于核对页名是否存在 | **是** |
| `wiki/` | 百科页面的原始 wikitext 快照 | **否**（gitignored） |
| `rulebook-index.json` | 规则书提取文本索引：来源 URL + 抓取日期 + 字节数 + SHA256 | **是** |
| `rulebook/` | 印刷规则书逐字提取文本快照（第三方 boardgame-rules 项目产物；官方原件待核） | **否**（gitignored） |
| `images/` | 本机缓存的图片（**当前不使用**：角色图在运行期热链百科，见 `docs/decisions/active.md` D-0007） | **否**（gitignored） |

## 为什么正文不提交

百科正文是他人作品；本仓库以 MIT 分发，不夹带别人的文本。
**进仓库的只有索引**——它让"某条引用的正文有没有被改过"可被复核，而不复制内容本身。

完整口径见 [../docs/standard/sources.md](../docs/standard/sources.md) §4。

## 怎么生成

由 `tools/fetch-wiki.ps1` 生成：

```powershell
pwsh -File tools/fetch-wiki.ps1
```

- 抓取范围在脚本里**显式列举**（剧本页 + 角色页 + 规则 / 机制页），禁止全站递归；扩展范围时手工加页名。
- 幂等：同一天重复运行产出逐字节一致的快照与索引；任一页失败则不写索引。
- 删掉 `wiki/` 后重跑即可完整重建（脚本自建目录）。

全站页名清单来自一次 `allpages` 列举的输出（2026-10-01，431 页），不随脚本更新；
将来需要重新列举时新增带日期的文件，不覆盖已有快照。

印刷规则书提取文本（官方原件不在手）由 `tools/fetch-rulebook.ps1` 生成：

```powershell
pwsh -File tools/fetch-rulebook.ps1
```

- 单一下载源（`-SourceUrl` 可换）；下载失败或内容为空时不写索引。
- 幂等：同一天重复运行产出逐字节一致的快照与索引。

## 相关阅读

- 来源与引用口径：`../docs/standard/sources.md`
- 未确证的规则：`../docs/standard/rulings.md`
- 术语：`../docs/standard/terminology.md`
