# AGENTS.md

本文件是仓库的**约束入口**：只路由与约束，不承载知识。动手前先读它链接的页面。
规范与工作流对齐参考项目 CUO；机器路径只写在 gitignored 的 `AGENTS.local.md`。

[REF] 文档体系 `docs/AGENTS.md` · 架构 `docs/architecture/current.md` ·
决策 `docs/decisions/active.md` · 术语 `docs/standard/terminology.md` ·
**裁定登记表** `docs/standard/rulings.md` · 来源与引用 `docs/standard/sources.md` ·
待办 `docs/backlog/README.md` · 验收 `docs/acceptance/AGENTS.md` ·
证据 `docs/evidence/verification.md` · 代理细则 `docs/development/agent-reference.md`

## 项目是什么

OpenClockTower 是《血染钟楼》(Blood on the Clocktower) 的线上平台：说书人端 + 每名玩家各自的
设备端，服务端权威状态，断线自动重连；**规则内核自动推演状态覆盖、生效顺序与链式反应**，
说书人保留自由裁量权——平台只做**记录 + 校验 + 推演**，不替说书人拍板。
首版覆盖《梦殒春宵》(Sects & Violets)。MIT 开源、小圈子自用。

## 构建与提交门禁

```bash
dotnet build OpenClockTower.slnx
dotnet test  OpenClockTower.slnx    # 含 NormativeGates.Tests：规范写成会失败的测试
dotnet format OpenClockTower.slnx
```

- `[GATE]` 三者全绿才可 commit。纯文档改动（不碰 `src/ tests/ tools/ web/`）跳过三者，审查 diff 后直接提交。
- `[GATE]` 收尾自主 `git add` + `git commit`（Conventional Commits），不留脏工作区。
  GPG 签名已全局开启，直接提交，不要定位 gpg、不要关闭签名。
- `[RULE]` 远端只保留有协作意义的分支；备份/实验分支留本地。

## 本项目的第一原则：规则不许凭记忆写

- `[CRITICAL]` 任何关于游戏规则的断言，必须给出**来源**：百科页名 + 抓取日期（口径见
  `docs/standard/sources.md`），或一条已登记的裁定。凭记忆或"常识"写规则是本项目的头号事故源。
- `[CRITICAL]` **查不到、拿不准、有冲突的，一律登记进 `docs/standard/rulings.md`，不许隐藏。**
  `Open` 状态的裁定，其默认行为在代码里必须带注释引用裁定号，禁止静默生效。
- `[CRITICAL]` `docs/standard/rulings.md` 是交付物的一部分：新增机制时必须同步维护它。

## 工程纪律

- `[CRITICAL]` 交付前过三关：**架构**（职责单一、依赖干净）、**测试**（行为有运行时验证）、
  **可维护性**（后人读得懂、改得动）。"能跑"是最低线，不是目标。
- `[CRITICAL]` 根因优先，不打补丁堆积；有正路就不走捷径，次优方案必须给出架构理由。
- `[CRITICAL]` 修复要**整族对齐**：修一个点，同时检查同类能力、同类模块是否有同病，同轮处理或记入 backlog。
- `[RULE]` 关键路径与每个逻辑分支必须可观测；日志带定位上下文（分支、状态、玩家/角色 id、输入、结果）。
- `[RULE]` 测试覆盖核心场景之后，必须主动覆盖边界、异常与失败路径。
- `[CRITICAL]` 纸面审查 = 没审查。每条关键链路自问"它在运行时怎么被证明？"
- `[RULE]` 大改动前先做架构审视（现状职责、目标域模型、依赖方向），提交完整方案再实施。

## 架构硬约束

- `[CRITICAL]` **内核确定性**：给定（状态 + 说书人裁定输入），结算必产出同一结果。内核内禁止
  时间、随机数、IO、日志副作用等不确定性来源；随机只允许出现在说书人裁定点。这是回放与撤销正确的前提。
- `[CRITICAL]` **状态属于玩家，不属于角色**。角色 / 阵营 / 生死 / 醉酒 / 中毒 / 疯狂六者相互独立，
  一个变了不带另一个变。任何把它们耦合的写法都是 bug。
- `[CRITICAL]` **信息隔离在服务端强制**：玩家端只能收到它该看到的投影，越权信息不下发。
- `[CRITICAL]` **同步的正路是补全初始条件**：重连 = 快照 + 从该序号起的全部事件。禁止"本地先跑
  N 秒再跟随""延迟窗口""保护期"这类掩盖起算点差异的写法。
- `[RULE]` 控制面与数据面分离；一文件一顶层类型，文件名 = 类型名；`[Handler(Key)]` + 反射注册，禁止巨型 switch。
- `[RULE]` 可变状态由持有者内部持有，只暴露窄接口；DI 服务不是全局可变状态仓。

## 工作流（硬顺序）

理解 → 机制清点 → 方案 + 自检表 → 实施 → 构建/门禁 → 运行时验证 → 独立对抗性自检 → 结构复审 → 提交。

- `[RULE]` 先按用户视角界定任务：复现步骤、期望行为、验收矩阵（角色 × 方向 × 视角 × 相关族）。
- `[RULE]` 缺陷必须先让期望的失败可见（回归测试或运行时探针先红），再改。
- `[RULE]` 每个工作项收口时，总结（本轮做了什么）与**交接提示词**（下个会话怎么接着做）都要给。
- `[RULE]` 上下文接近耗尽时在阶段边界停下交接，不要在耗尽状态下产出浅层工作。

## 约定

- 语言：代码、标识符、注释、提交信息用英文；人工文档与界面用中文；术语以
  `docs/standard/terminology.md` 为准（角色名「英文 slug + 中文名」成对）。详见 `docs/decisions/active.md`。
- 提交信息：`type(scope): summary`，类型限 `feat fix docs test chore refactor perf revert build ci style`。
- 空目录保留用 0 字节 `.gitkeep`。
- 不提交官方美术资源；图片在运行期外链。见 `docs/decisions/active.md`。
- **绝不提交**任何机器绝对路径（盘符、UNC、用户目录）；本地路径只进 `AGENTS.local.md`。

## 首版明确不做

多实例与高并发、多级缓存、专用服务器集群、语音/视频通话（玩家用外部语音）、
AI 全自动说书人、社区剧本与实验性角色的全量覆盖。
