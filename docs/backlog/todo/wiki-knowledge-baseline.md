# 百科知识基线与引用索引

- Status: Todo
- Priority: High
- Depends on: 无

## 要解决的问题

规则依据现在**散落在临时目录里，没有索引、没有校验、无法复核**：

1. 已抓取的 81 个页面在 `%TEMP%\dsh-openct-wiki\`，进程一清就没了；
2. 全站 431 页清单只存在于一次命令输出里；
3. `docs/standard/sources.md` 规定的 `references/wiki-index.json` 还不存在，
   于是"某条引用有没有被改过"根本无法检查；
4. `docs/standard/terminology.md` §9 的《梦殒春宵》25 角色**只确认了页名存在**，
   角色类型归属（镇民/外来者/爪牙/恶魔）尚未与百科剧本页核对——按本项目的规则，
   这种状态**不许**写进角色数据。

## 要做的事

1. 写 `tools/fetch-wiki.ps1`：走 MediaWiki API 抓取指定页面 → 落 `references/wiki/`（已 gitignore），
   并生成/更新 `references/wiki-index.json`（页名 + 抓取日期 + 字节数 + SHA256）。
   脚本必须可重复运行且幂等；抓取范围必须**显式列举**，禁止全站递归。
2. 抓全《梦殒春宵》相关页面：剧本页 + 25 个角色页 + 《相克规则》中与本剧本角色相关的条目。
3. 用剧本页核对 25 角色的**类型归属**，回填 `terminology.md` §9 与英文 slug。
4. 从每个角色页抽取「**规则细节**」与「**范例**」两节，建立：
   - 规则细节 → 实现依据清单（指向 `docs/standard/sources.md` 的引用格式）；
   - 范例 → 回归测试用例清单（供 `tests/Kernel.Tests` 使用）。
5. 扫一遍 25 个角色，把所有**百科未覆盖 / 说法冲突**的点登记进
   `docs/standard/rulings.md`（尤其 `R-0004`「能力未正常生效」口径）。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 运行 `tools/fetch-wiki.ps1` | `references/wiki-index.json` 生成，含 SHA256 | 生成的索引文件 |
| 2 | 重复运行第二次 | 索引内容稳定（除日期外无变化） | 两次索引的 diff |
| 3 | 删掉 `references/wiki/` 后运行 | 完整重建，不缺页 | 重建后的文件计数 |
| 4 | 核对 25 角色类型 | 与百科剧本页逐条一致 | `terminology.md` §9 更新 |
| 5 | 每个角色页有「规则细节」 | 每条都落到引用清单或 `rulings.md` | 清单覆盖率 |
| 6 | 抽取角色页「范例」 | 每条范例变成一条可执行的测试用例（先只登记，不实现） | 用例清单 |
| 7 | 索引条目核对 | 随机抽 3 条引用，正文 SHA256 与索引一致 | 校验输出 |

## 决定与依据

- 抓取口径与不提交正文的理由：`docs/standard/sources.md` §4
- 百科内部可信度层级（哪一节算依据、哪一节只是策略建议）：`docs/standard/sources.md` §2
- 角色类型未核对前不许进数据：`docs/standard/terminology.md` §9

## 备注

「华灯初上」系列与社区剧本**不在**本票据范围内，见 `docs/decisions/active.md` D-0003。
