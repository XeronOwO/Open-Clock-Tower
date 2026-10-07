# 开局分配的角色下拉列着 5 名旅行者（服务端会拒）

- Status: Todo
- Priority: Low（界面白列了不可用的选项；服务端**已经拦下**，不构成规则违反）
- Depends on: 无
- 咬出它的一轮：`docs/backlog/done/character-change-traveller-boundary.md`（R-0060 的整族核对）

## 要解决的问题

`web/src/features/storyteller/AssignmentControl.vue` 的角色下拉直接遍历整份花名册：

```vue
<option v-for="profile in ROSTER" :key="profile.slug" :value="profile.slug">
  {{ profile.name }}（{{ profile.slug }}，{{ profile.type }}）
</option>
```

`ROSTER` 是 30 条（含 5 名旅行者），而开局分配**只覆盖镇民 / 外来者 / 爪牙 / 恶魔**：
`src/OpenClockTower.Application/AssignmentGate.cs` 以 `legality.character_not_assignable`
明确拒绝旅行者（"旅行者的加入走专属流程"）。于是说书人能选中一个注定被拒的选项。

**与 R-0060 的关系**：这个下拉是"候选表取自整张花名册"的同款形态，但**后果不同**——
它只是把一个不合法选项摆在眼前，服务端仍然拦得住；麻脸巫婆那条是真的能把旅行者造出来。

## 先要问清的一件事（别急着改）

`R-0046` 承认"开局带旅行者"的局（总人数 > 15 时超出的人必须是旅行者）。所以这里有两种口径：

- **甲**：下拉过滤掉旅行者——旅行者一律走"旅行者加入"流程（与本票现状一致，改动最小）；
- **乙**：下拉支持旅行者，但把它接到旅行者流程（分配阶段只记"这几席是旅行者"，开局后由说书人
  逐名选旅行者角色并私下定阵营，即 D-0022 / D1 的流程）。

先按 R-0046 核实"开局就带旅行者"在平台上的落地路径，再决定改哪一边；**不许两边都做一半**。

## 验收（改哪一边都要有运行时证据）

- 界面级：装置读 `AssignmentControl` 的角色下拉，断言"旅行者不在其中"（甲）或"选了旅行者会走
  旅行者流程"（乙）——**阴性方向也要**：非旅行者一个都不能少。
- 服务端保持原判据（`AssignmentGate` 的拒绝路径不要因为界面改了而放松）。

## 相关阅读

- 界面：`web/src/features/storyteller/AssignmentControl.vue` · 权威数据 `web/src/display/labels.ts`
- 服务端闸：`src/OpenClockTower.Application/AssignmentGate.cs`
- 口径：`docs/standard/rulings.md` R-0046（开局带旅行者的配板边界）· R-0060（旅行者这条线）
