using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 艺术家白天提问的三道小闸（R-0040）：身份、白天前置与"未结清挡推进"。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁）：与触发型裁定的推进闸同姿态，
/// 但放行提名 / 投票等白天内部流程——裁定只要求「必须结清才能推进阶段 / 收口白天」，
/// 说书人的强推 / 收口仍是兜底（D-0011 / D-0014）。
/// </remarks>
internal static class ArtistQuestionGate
{
    /// <summary>身份闸：艺术家的提问只能由持席位的玩家发出（D-0012：命令面不自称身份）。</summary>
    internal static CommandRejection? IdentityRejection(Actor actor) =>
        actor.Kind == ActorKind.Player && actor.Seat is not null
            ? null
            : new CommandRejection
            {
                Code = "identity.player_only",
                Message = "只有玩家本人可以向说书人提问",
                Gate = "identity",
            };

    /// <summary>阶段闸：提问只在白天开着时可用；具体规则（是不是艺术家、用没用过）在内核里判。</summary>
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
