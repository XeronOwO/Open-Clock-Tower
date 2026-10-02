# 结算结论的三态表达：把「未知」写进契约

- Status: Todo
- Priority: Low
- Depends on: 无

## 要解决的问题

`EffectDto.Terminated` / `AbilityUseDto.Effective` / `AbilityResolutionDto.Effective` 是
**必填 bool**，wire 上表达不了"服务端没给结论"这第三种状态。

- 生产者目前**每条路径都显式赋值**（`ProjectionMapper` 逐条走查过：`use.Effective`、
  `resolution.Effective`、`effect.IsTerminated` / `false`），所以今天不存在"未知"；
- 但契约本身允许"漏字段"这种坏载荷存在，而前端拿到它只能退化成 `false`——
  于是面板会把"服务端没说"渲染成"未正常生效"。这正是
  `web/src/display/format.ts` 与 `todo/storyteller-step-insights.md` 反复强调的
  「未观测 ≠ 默认值」在**呈现层唯一还没落实**的一格。

## 验收矩阵

| # | 场景 | 期望 | 依据 | 证据 |
|---|---|---|---|---|
| 1 | 载荷缺 `effective` / `terminated` | 面板显示"结论未知"，不显示"未生效 / 未终止" | `docs/architecture/current.md` §4.4、票据「说书人上帝视角」 | 待补 |
| 2 | 载荷给出结论 | 面板显示结论（与今天一致） | 同上 | 待补 |

## 决定与依据

- 2026-10-02 独立复核提出"呈现层把缺数据落成结论"；当轮判断为**生产者不会漏**，
  因此没有把三处改 `bool?`（那会连带 8 处既有集成断言），而是记进本票：
  要么把契约改成三态（`bool?` + 前端"未知"文案），要么显式规定"缺失 = 协议违规"
  并在反序列化处拒绝。两条路都要求先定口径。
