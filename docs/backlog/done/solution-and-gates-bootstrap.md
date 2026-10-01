# 建解决方案、项目骨架与门禁工程

- Status: Done
- Priority: High
- Depends on: 无

## 要解决的问题

仓库现在只有规范与文档，**没有任何可编译的工程**：`dotnet build/test/format` 跑不了，
`AGENTS.md` 里写下的三条门禁是空头承诺，而"把规范写成会失败的测试"（NormativeGates）
无从落地。没有门禁，后面所有规则代码都只能靠自觉——这正是本项目要避免的。

## 已落地

- `OpenClockTower.slnx`（`.slnx` 格式）与 7 个项目：`Kernel` / `Rules` / `Application` /
  `Contracts` + `Kernel.Tests` / `NormativeGates.Tests` / `Integration.Tests`。
- `Directory.Build.props`：TFM（`net10.0`）**只出现一次**，`Nullable`、`ImplicitUsings`、
  `TreatWarningsAsErrors`、`InvariantGlobalization` 全仓库统一。
- `Directory.Packages.props`：中央包版本管理，版本号只出现在一处。
- 依赖方向接进 csproj：`Application → Rules → Kernel`，`Contracts` 独立。
- 内核首批领域类型：`SeatState` + `CharacterId` / `Alignment` / `LifeState` /
  `DrunkState` / `PoisonState`（六状态正交）。
- 四条规范门禁 + 一条程序集级依赖检查。

## 验收记录（代理运行，2026-10-01）

环境：.NET SDK 10.0.401、git 2.47.0.windows.2、Node v24.14.1。

| # | 场景 | 期望 | 实测 | 结论 |
|---|---|---|---|---|
| 1 | `dotnet build OpenClockTower.slnx` | 0 error 0 warning | 0 警告 0 错误 | 通过 |
| 2 | `dotnet test OpenClockTower.slnx` | 全绿 | 12 通过 / 0 失败（Kernel 6 + Gates 5 + Integration 1） | 通过 |
| 3 | `dotnet format --verify-no-changes` | 无改动 | exit 0 | 通过 |
| 4 | 在 Kernel 里写 `DateTime.Now` | 纯净门禁失败 | RED（G-01） | 通过 |
| 5 | 让 Kernel 引用上层项目 | 依赖方向门禁失败 | RED（G-02，用 Contracts 注入，避免造成循环引用） | 通过 |
| 6 | 把指令文件撑过 5,120 字节 | 体量门禁失败 | RED（G-04） | 通过 |
| — | 额外：一个文件写两个顶层类型 | 形状门禁失败 | RED（G-03） | 通过 |

> 第 4–6 行是**必须做**的：门禁若从未见红，就不知道它到底在不在工作。
> 每条见红探针都在同一脚本内立即还原，还原后复跑全部 12 个测试仍全绿。

## 过程中发现并修掉的真实缺陷（不是绕过去）

1. **纯净门禁扫到了构建产物**：`obj/**/*.GlobalUsings.g.cs` 因 `ImplicitUsings` 含
   `System.IO` / `System.Net`，使门禁在"已构建"状态下误报，而在"干净"状态下通过——
   同一份代码两种结论。已改为只扫手写源码、跳过 `bin` / `obj`。
2. **绝对路径门禁扫了未跟踪文件**：把 gitignored 的 `AGENTS.local.md` 报成违规，
   而按约定那正是放机器路径的地方——门禁自相矛盾，注定被关掉。
   已改为问 `git ls-files`，判定语义回到"提交进版本库的文件"。
3. **文档自身违反规则**：`docs/development/agent-reference.md` 举例时写了盘符字面量。
   已改写为不含字面量的表述。

## 决定与依据

- 分层与依赖方向：`docs/architecture/current.md` §1
- 内核确定性与禁止清单：`docs/decisions/active.md` D-0008
- 门禁必须先见红：`AGENTS.md`「工作流」
- 指令文件体量上限：`docs/AGENTS.md` §6

## 遗留

- `Integration.Tests` 目前只有 1 条"程序集级依赖方向"检查。真正的多客户端端到端验收
  要等第一个可运行会话（见 [自动步骤机与操作请求](operation-request-step-machine.md)）。
- `dotnet format` 目前只在本地跑；CI 尚未建立（`AGENTS.local.md` 已记录）。
