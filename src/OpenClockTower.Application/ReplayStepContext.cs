using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 一个复盘步骤的投影上下文：事件本身 + 折叠前后两侧的派生状态（同源重折，D-0020）。
/// </summary>
/// <remarks>
/// 折叠器与实时视图完全相同（<see cref="StepMachine.Apply"/> / <see cref="GameStateMachine.Apply"/>），
/// 因此复盘文案与说书人账本口径一致（票据矩阵行 4），顺序保真与"按住事件流重建"同源（行 6）。
/// </remarks>
public sealed record ReplayStepContext
{
    /// <summary>本步背书的事件与序号。</summary>
    public required StoredEvent Stored { get; init; }

    /// <summary>折叠本条事件**之前**的步骤机状态；账事件先于任何阶段时为 null。</summary>
    public required StepMachineState? MachineBefore { get; init; }

    /// <summary>折叠本条事件**之后**的步骤机状态。</summary>
    public required StepMachineState? MachineAfter { get; init; }

    /// <summary>折叠本条事件之前的状态账。</summary>
    public required GameState StateBefore { get; init; }

    /// <summary>折叠本条事件之后的状态账。</summary>
    public required GameState StateAfter { get; init; }

    /// <summary>本步所属阶段：按折叠后的计划取，未开局（账事件）为 null。</summary>
    public GamePhase? Phase => MachineAfter?.Plan?.Phase ?? MachineBefore?.Plan?.Phase;
}
