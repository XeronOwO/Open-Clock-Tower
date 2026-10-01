using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 状态快照：用于重建校验与恢复（D-0010 提到的定期快照）。
/// </summary>
/// <remarks>
/// 事件仍是唯一事实来源：快照只是派生结果，任何时候都可以从事件流重算出来。
/// </remarks>
public sealed record StoredSnapshot
{
    /// <summary>快照对应的事件序号。</summary>
    public required long Sequence { get; init; }

    /// <summary>快照时刻的步骤机状态；尚未开阶段时为 null。</summary>
    public required StepMachineState? Machine { get; init; }

    /// <summary>快照生成时刻。</summary>
    public required DateTimeOffset RecordedAt { get; init; }
}
