# 三个装置仍按已退役的 `.mono` 结构读玩家信息行：E31 改版后整段读空

- Status: Done（2026-10-04）
- Priority: Medium
- Depends on: 无（先于本轮发现：E31 批次没重跑这三个装置；见 `done/ui-layout-and-onboarding.md` 与 `done/accounts-device-section-selector.md`）

## 要解决的问题

E31（`done/ui-layout-and-onboarding.md`）把玩家页「我收到的信息」的一行从「英文 slug + 内容」
改成「能力标签：内容」——`web/src/features/player/PlayerPanel.vue` 现为
`<li data-information-index="…"><strong>{{ characterLabelOf(ability) }}</strong>：{{ content }}</li>`，
其中 `characterLabelOf` 渲染「中文名（slug）」（E31 的友好化目标，见其票据行 4 / 5）。
三个装置仍按旧结构读行：

- `tools/verify-retro-info.mjs`、`tools/verify-death-triggers.mjs`、`tools/verify-seamstress-artist.mjs`
  都有一份 `readPlayerInformationRows`：能力读 `item.querySelector('.mono')`、内容读
  `item.querySelectorAll('span')[1]`——新 DOM 里两者都不存在，读出来恒为 `{ability:'', content:''}`。

实测（2026-10-04，`main @ e6917db` 的 `git stash` 基线，即**不含任何新改动**）：
retro-info 失败 6 / 53、seamstress-artist 失败 2 / 82、death-triggers 失败 1 / 70，
三处失败指纹相同：`count=1；行=[{"index":"0","ability":"","content":""}]`。
基线同红 → 这是既有缺陷；但它让这三条链路的真机回归目前**不可用**。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | 读法跟进真实 DOM | helper 读「`<strong>` 可见标签 + 分隔符后的内容」；断言以完整可见标签锚定能力身份，不再依赖 `.mono` / span 层级 | 代码 diff |
| 2 | 三装置回归 | 各自一次迭代档运行全绿（retro-info 53 / seamstress-artist 82 / death-triggers 70） | 三次运行日志 |
| 3 | 影响面清点 | 全仓 grep：按旧结构读「玩家信息行」的只剩这三处；其余 `.mono` 读的是说书人面板 / 状态条，不在本票 | grep 清单 |

## 决定与依据

- **不向玩家 UI 加 `data-ability` 测试钩子**：装置纪律是「从真界面读」，E31 之后玩家看到的能力身份
  就是「中文名（slug）」；三装置本来就大量使用中文锚点（「博学者」「得知两名玩家」等），
  标签即身份，加钩子反而让装置继续断言一个玩家看不到的字段；
- **内容 = 行文本减去标签与分隔符**（`：`/`:`）：不依赖 `span[1]` 的层级，下一次改版最多断一处；
- 只动 `tools/`，不碰 `web/`：本票不需要产品代码变化。

## 结果（2026-10-04 · 三次迭代档运行）

| # | 判据 | 实测 | 结论 |
|---|---|---|---|
| 1 | 新读法 | 三份 helper 统一读 `{index, label, content}`：`label` = `<strong>` 文本（完整可见标签），`content` = 行文本减去标签与 `：`；retro-info 的断言经 `INFO_LABELS` 映射到「卖花女孩（flowergirl）/ 城镇公告员（town-crier）/ 神谕者（oracle）」逐字比对 | 通过 |
| 2 | 修复后回归 | `retro-info` 53 项 · `seamstress-artist` 82 项 · `death-triggers` 70 项，各自一次迭代档**全绿**（退出码 0） | 通过 |
| 3 | 影响面 | 修复前：3 装置各一处 `querySelector('.mono')` + `querySelectorAll('span')[1]`；修复后全仓该类读法 0 处；残留 2 处 `.mono` 读的是说书人状态条单元格（`StatusStrip` 仍有该元素），与信息行无关 | 通过 |

诚实记录：

- 首跑修复时假定标签只有中文名 → 实际是「中文名（slug）」：三处断言按**玩家实际看到的完整标签**
  收紧后全绿（数值证据见日志行 `行=[{"index":"0","label":"卖花女孩（flowergirl）","content":"恶魔参与了投票"}]`）；
- 日志：`artifacts/web/bounded-retro-info-fixed.log`、`bounded-seamstress-artist-fixed.log`、
  `bounded-death-triggers-fixed.log`；基线红证据：`baseline-retro-info.log` 等；
- E32 补证（2026-10-04）：三装置在本票读法落地后的冻结版 `407eea2` 上重跑**取证档**
  （`--quota 2 --screenshots-all`），**53 / 82 / 70 全绿、退出码 0**；截图 `retro-*` / `limitinfo-*` /
  `deathtrigger-*` 为本次运行写入（关键 9 张已复核），批次记录见 `docs/acceptance/batches.md`。
