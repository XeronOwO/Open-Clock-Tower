# references/ — 外部资料与本机缓存

本目录放**游戏规则的原始出处**。它回答一个问题：**这条规则是从哪儿查来的？**

| 路径 | 内容 | 提交进仓库 |
|---|---|---|
| `wiki-index.json` | 百科抓取索引：页名 + 抓取日期 + 字节数 + SHA256 | **是** |
| `wiki/` | 百科页面的原始 wikitext 快照 | **否**（gitignored） |
| `images/` | 本机缓存的图片（若将来需要） | **否**（gitignored） |

## 为什么正文不提交

百科正文是他人作品；本仓库以 MIT 分发，不夹带别人的文本。
**进仓库的只有索引**——它让"某条引用的正文有没有被改过"可被复核，而不复制内容本身。

完整口径见 [../docs/standard/sources.md](../docs/standard/sources.md) §4。

## 怎么生成

由 `tools/fetch-wiki.ps1` 生成（尚未实现，见
[../docs/backlog/todo/wiki-knowledge-baseline.md](../docs/backlog/todo/wiki-knowledge-baseline.md)）。
脚本走 MediaWiki API，抓取范围必须**显式列举**，禁止全站递归。

## 相关阅读

- 来源与引用口径：`../docs/standard/sources.md`
- 未确证的规则：`../docs/standard/rulings.md`
- 术语：`../docs/standard/terminology.md`
