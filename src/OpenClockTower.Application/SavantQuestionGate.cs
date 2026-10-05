using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 博学者白天要信息的两道小闸（R-0057）：身份与白天前置。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（与 <see cref="ArtistQuestionGate"/> 同款）：
/// 具体规则（是不是博学者、今天要过没有）在内核里判；「未结清挡推进」在
/// <see cref="PendingChoiceGate.SavantQuestionPending"/>。
/// </remarks>
internal static class SavantQuestionGate
{
    /// <summary>身份闸：要信息只能由持席位的玩家发出（D-0012：命令面不自称身份）。</summary>
    internal static CommandRejection? IdentityRejection(Actor actor) =>
        actor.Kind == ActorKind.Player && actor.Seat is not null
            ? null
            : new CommandRejection
            {
                Code = "identity.player_only",
                Message = "只有玩家本人可以向说书人要信息",
                Gate = "identity",
            };

    /// <summary>阶段闸：要信息只在白天开着时可用；具体规则在内核里判。</summary>
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
