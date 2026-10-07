# 麻脸巫婆装置的角色选项条数断言过期（25 vs 30）

- Status: Todo
- Priority: Low（**本批之前就红**的陈旧断言，不是产品缺陷；装置读数与产品行为都对不上是它自己的错）
- Depends on: 无

## 要解决的问题

`tools/verify-pit-hag.mjs` 有一条写死的条数断言：

```js
check('第二维渲染 25 个角色选项（整张角色列表）', (await secondaryOptions.count()) === 25, …)
```

实测渲染 **30** 个（`渲染 30 个`）。原因不在产品侧：麻脸巫婆的候选角色来自
`PitHagAbility.SelectableCharacters()`，它返回的是**整张花名册** `SectsAndVioletsRoster.All`——
25 名非旅行者 **+ 5 名旅行者 = 30**（旅行者在 D1 那批进的花名册）。断言写下的那天花名册还是 25 条。

**归因证据（2026-10-07，批次 E60 收尾总跑时咬出）**：在**本批之前的 HEAD（`8ba2776`）**上跑同一个装置，
同样是 `失败 1 / 34`、同一条断言、同样渲染 30 个——与本批无关，是上一次动花名册时没有回头跑这个装置。

## 验收（改哪一边都行，但要说清为什么）

- 甲：断言改成**从花名册派生**（像其它装置那样按 `--assign` / 花名册算期望条数），
  以后再进角色它自己跟着走；同时把"含恶魔、不因在场被过滤"那两条保留。
- 乙：断言改成"整张花名册都在"的**语义**判据（例如候选集合等于 `SelectableCharacters()`），
  不写具体数字。

**先红后绿**：改完在**当前 HEAD** 上跑 `node tools/verify-pit-hag.mjs`（辅助装置量级，实测 24s）应全绿；
再故意把期望值改错一次确认它会红。

## 相关阅读

- 装置：`tools/verify-pit-hag.mjs`（段 `5/8`）
- 花名册：`src/OpenClockTower.Rules/SectsAndVioletsRoster.cs`、`web/src/display/labels.ts`（镜像门禁对账）
- 咬出它的批次：`docs/acceptance/batches.md` 的 E60（"装置总跑咬出的两处**历史**红"）
