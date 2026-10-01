# 建解决方案、项目骨架与门禁工程

- Status: Todo
- Priority: High
- Depends on: 无

## 要解决的问题

仓库现在只有规范与文档，**没有任何可编译的工程**：`dotnet build/test/format` 跑不了，
`AGENTS.md` 里写下的三条门禁是空头承诺，而"把规范写成会失败的测试"（NormativeGates）
无从落地。没有门禁，后面所有规则代码都只能靠自觉——这正是本项目要避免的。

## 要做的事

1. 建 `OpenClockTower.slnx`，按 `docs/architecture/current.md` 的分层建空项目：
   `Kernel` / `Rules` / `Application` / `Contracts` / `Server`，以及
   `tests/Kernel.Tests` / `tests/NormativeGates.Tests` / `tests/Integration.Tests`。
2. `Directory.Build.props`：锁定 TFM、`Nullable=enable`、`TreatWarningsAsErrors`、
   `LangVersion`、`InvariantGlobalization` 等；`Directory.Packages.props` 启用中央包管理。
3. `NormativeGates.Tests` 落地**第一批**门禁（每条都必须先能失败，再谈通过）：
   - **内核纯净门禁**：`Kernel` 项目不得引用任何 IO/时间/随机 API
     （`DateTime.Now`、`Random`、`Guid.NewGuid`、`File`、`HttpClient` 等出现即失败）。
     依据 `docs/decisions/active.md` D-0008。
   - **依赖方向门禁**：`Kernel` 不得引用 `Rules`/`Application`/`Server`；`Rules` 不得引用上层。
   - **一文件一顶层类型门禁**。
   - **绝对路径门禁**：仓库中不得出现机器绝对路径（盘符 / UNC / 用户目录）。
     依据 `AGENTS.md`「约定」。
   - **文档体量门禁**：每份指令文件（`AGENTS.md`、`docs/**/AGENTS.md`）≤ 5,120 字节。
     依据 `docs/AGENTS.md` §6。
4. `dotnet format` 纳入门禁并在 CI 之外也能本地跑通。

## 验收矩阵

| # | 场景 | 期望 | 证据 |
|---|---|---|---|
| 1 | `dotnet build OpenClockTower.slnx` | 0 error 0 warning | 构建输出 |
| 2 | `dotnet test OpenClockTower.slnx` | 全绿 | 测试输出 |
| 3 | `dotnet format --verify-no-changes` | 无改动 | 命令输出 |
| 4 | 在 `Kernel` 里临时写一行 `DateTime.Now` | NormativeGates **失败** | 先红后绿的记录 |
| 5 | 在 `Kernel` 的 csproj 里临时加 `ProjectReference` 到 `Server` | 依赖方向门禁**失败** | 先红后绿的记录 |
| 6 | 临时把 `docs/AGENTS.md` 撑到 5000+ 字节 | 体量门禁**失败** | 先红后绿的记录 |

> 第 4–6 行是**必须做**的：门禁若从未见红，就不知道它到底在不在工作。

## 决定与依据

- 分层与依赖方向：`docs/architecture/current.md` §1
- 内核确定性的理由与禁止清单：`docs/decisions/active.md` D-0008
- 门禁必须先见红：`AGENTS.md`「工作流」——"缺陷必须先让期望的失败可见"

## 备注

TFM 与 SDK 版本在本票据内确定后，回写 `README.md`「构建」一节与 `AGENTS.local.md`。
