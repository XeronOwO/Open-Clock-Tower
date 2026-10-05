using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 杂耍艺人公开猜测的两道小闸（R-0057-B）：身份与白天前置。
/// </summary>
/// <remarks>
/// 与 <see cref="SavantQuestionGate"/> / <see cref="ArtistQuestionGate"/> 同款：具体规则
/// （是不是杂耍艺人、是不是首个白天、有没有猜过）在内核里判；这里只挡"谁在说"与"什么时候能说"。
/// </remarks>
internal static class JugglerGuessGate
{
    /// <summary>身份闸：公开猜测只能由持席位的玩家发出（D-0012：命令面不自称身份）。</summary>
    internal static CommandRejection? IdentityRejection(Actor actor) =>
        actor.Kind == ActorKind.Player && actor.Seat is not null
            ? null
            : new CommandRejection
            {
                Code = "identity.player_only",
                Message = "只有玩家本人可以公开猜测",
                Gate = "identity",
            };

    /// <summary>阶段闸：猜测只在自己的首个白天可用；具体规则在内核里判。</summary>
    internal static CommandRejection? DayRequirement(StepMachineState? machine) =>
        machine is null || machine.Plan.Phase != GamePhase.Day || machine.Day?.OpenDay is null
            ? new CommandRejection
            {
                Code = "phase.not_open_day",
                Message = "现在不是白天，或白天已经结束",
                Gate = "phase",
            }
            : null;
}
