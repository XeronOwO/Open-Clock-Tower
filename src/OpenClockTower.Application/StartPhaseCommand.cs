using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>宿主开启一个新阶段（计划由服务端构造 / 配置提供，绝不来自客户端）。</summary>
public sealed record StartPhaseCommand : GameCommand
{
    /// <summary>本阶段的完整步骤表（含空槽位）。</summary>
    public required StepPlan Plan { get; init; }

    /// <summary>阶段开始时的控制模式；说书人可以在接管下开启新阶段（D-0014）。</summary>
    public ControlMode Control { get; init; } = ControlMode.Automatic;
}
