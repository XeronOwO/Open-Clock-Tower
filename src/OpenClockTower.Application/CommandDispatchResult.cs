using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>一次命令的内核产出：新步骤机状态（阶段开始前为 null）+ 事件 + 可选的拒绝。</summary>
internal sealed record CommandDispatchResult(
    StepMachineState? Machine,
    IReadOnlyList<GameEvent> Events,
    CommandRejection? Rejection)
{
    /// <summary>构造一个拒绝结果。</summary>
    internal static CommandDispatchResult Rejected(CommandRejection rejection) => new(null, [], rejection);
}
