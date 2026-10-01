namespace OpenClockTower.Kernel;

/// <summary>步骤机处理输入的结果类型。</summary>
public enum StepMachineOutcomeKind
{
    /// <summary>输入被接受，产出了事件（可能为空，例如没有匹配依赖的座位变化）。</summary>
    Applied,

    /// <summary>输入被拒绝：状态不变、事件为空（拒绝也要有原因，不许静默吞掉）。</summary>
    Rejected,
}
