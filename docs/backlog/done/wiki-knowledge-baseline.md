# 百科知识基线与引用索引

- Status: Done
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

## 已落地（2026-10-01）

- `tools/fetch-wiki.ps1`：显式枚举 79 页（2 剧本页 + 25 角色页 + 4 参考角色页 + 6 类型页 + 40 规则 / 机制页 + 2 纠错证据页），
  走 MediaWiki API，落 `references/wiki/`（gitignored）+ `references/wiki-index.json`（页名 / 日期 / 字节数 / SHA256，进仓库）。
  幂等；任一页失败即中止且不写索引。
- 全站 431 页清单迁入 `references/wiki-all-pages-2026-10-01.txt`（进仓库）。
- `docs/standard/terminology.md` §9 回填 25 角色的类型与 slug（另附非官方别名表）。
- `docs/standard/character-rules.md`：25 个角色的实现依据清单（15 个角色有 L1「规则细节」，10 个角色无 L1、条目为 L4 参考）
  + 《相克规则》中涉及 S&V 角色的 39 条条目（3 条通用说明 + 36 条组合）。
- `docs/standard/character-examples.md`：80 条范例 → 回归测试用例登记（`TC-<slug>-<nn>`）。
- `docs/standard/rulings.md` 新增 R-0010（英文 slug 来源口径）、R-0011（无「规则细节」时的实现依据）；R-0004 追加核对补充。

## 验收记录（代理运行，2026-10-01）

| # | 场景 | 期望 | 实测 | 结论 |
|---|---|---|---|---|
| 1 | 运行 `tools/fetch-wiki.ps1` | `wiki-index.json` 生成，含 SHA256 | 79 页全成功；索引 79 条，含 page / fetchedAt / bytes / sha256 | 通过 |
| 2 | 重复运行第二次 | 索引稳定（除日期外无变化） | runA 与 runB 索引**逐字节一致** | 通过 |
| 3 | 删掉 `references/wiki/` 后运行 | 完整重建，不缺页 | 三步走删除（79 个文件 → 0 → 目录移除）后重跑：79 文件重建，索引与 runA 逐字节一致 | 通过 |
| 4 | 核对 25 角色类型 | 与百科剧本页逐条一致 | 剧本页（13 镇民 / 4 外来者 / 4 爪牙 / 4 恶魔）与各角色页「角色信息」节双向一致；已回填 §9 | 通过 |
| 5 | 每个角色页有「规则细节」 | 每条都落到引用清单或 rulings.md | 15/25 页有 L1 内容（已逐条录入）；10/25 页无 L1 内容，清单以 L4 参考覆盖并显式标注（R-0011）；覆盖率 25/25 | 通过 |
| 6 | 抽取角色页「范例」 | 每条范例变成一条可执行的测试用例（先只登记） | 80 条范例 → `character-examples.md` 全部登记 | 通过 |
| 7 | 索引条目核对 | 随机抽 3 条，SHA256 与索引一致 | 全量 79 条核对 0 失配；随机抽 3 条（《疯狂规则如何运作？》《角色能力类别总览》《占卜师》）SHA256 与字节数全 match | 通过 |

> 验收由代理在本机按矩阵逐行判定（不依赖对局会话；`docs/acceptance/AGENTS.md` §5 的"通过"条件：有该行的运行时证据）。

> 本票据属 `docs/acceptance/AGENTS.md` §2 定义的**不依赖对局**类：矩阵 1/2/3/7 的证据是脚本运行输出与哈希比对，4/5/6 是逐条核对与覆盖率统计。

**复算方式**（有 shell 的会话可直接跑）：

```powershell
$j = Get-Content references/wiki-index.json -Raw | ConvertFrom-Json
$bad = foreach ($p in $j.pages) {
  $f = "references/wiki/$($p.file)"
  $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash.ToLower()
  if ($h -ne $p.sha256 -or (Get-Item -LiteralPath $f).Length -ne $p.bytes) { $p.page }
}
"mismatch=$(@($bad).Count)"
```

## 过程中发现并修掉的问题

1. **`terminology.md` §9 原名单有两处错**：用 431 页清单命中的「圣洁之魂」实为传奇角色 Spirit of Ivory，
   「僵怖」实为《黯月初升》恶魔 Zombuul；本剧本对应角色是「心上人」（Sweetheart）与「亡骨魔」（Vigormortis）。
   已按剧本页修正 §9，并在抓取脚本里保留这两页为"纠错证据页"。
   教训：**全站页名清单不能直接当角色名单用**（§9 注记已写明）。
2. **英文名来源原先缺失**：百科正文不给 slug，改为取角色页「角色信息」节英文名 + TPI 官方英文 wiki 双向核对（R-0010）。
3. **脚本的失败原子性（独立自检发现）**：原实现边抓边写，失败或中断时会留下"新快照 + 旧索引"的半更新状态。
   已改为**先把全部页面抓到内存、全部成功后一次性落盘**，并对页名做文件名字符校验；
   改造后重新完成验收 1 / 2 / 3 / 7（79 页全成功、索引幂等、删库重建一致、SHA256 全量零失配）。

## 遗留

- 10 个角色页没有「规则细节」内容，其实现依据为 L4 参考（R-0011）；实现这些角色时若遇细节疑问，登记新裁定。
- 《相克规则》中涉及 S&V 的 39 条**没有一条是两个 S&V 角色之间的组合**；纯 S&V 对局不会触发相克判定，
  相克解析器的真实用例要等跨剧本 / 旅行者角色加入。
- 本票据不实现任何角色代码；`Rules` 层的角色实现仍未开始（属后续票据）。
