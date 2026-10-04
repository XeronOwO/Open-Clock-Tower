using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 「挂起挡推进」闸族（D-0011 / D-0014）：触发来源的请求、触发型裁定与艺术家提问未结清时，
/// 推进 / 收口类命令一律被拒——选择必须尽快做出，说书人的代填 / 作废 / 强推是兜底。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁）：三条闸的名单与理由各不相同，
/// 放在一处便于整族对齐（漏一条就是「带着挂起静默推进」）。艺术家提问只挡阶段推进 / 收口白天，
/// 放行提名 / 投票等白天内部流程——裁定不阻断白天玩法（R-0040）。
/// </remarks>
internal static class PendingChoiceGate
{
    /// <summary>触发来源的请求（如呆瓜的死亡选择，R-0027）未了结。</summary>
    internal static CommandRejection? TriggerRequestPending(StepMachineState? machine, GameCommand command)
    {
        if (machine?.PendingRequest is
            {
                Status: OperationRequestStatus.Pending,
            } triggerPending
            && triggerPending.Origin.Kind == OperationRequestOriginKind.Trigger
            && IsAdvancing(command))
        {
            return new CommandRejection
            {
                Code = "phase.trigger_choice_pending",
                Message = $"还有一条未了结的选择（{triggerPending.Addressee.Value} 号）：先作答，或由说书人代填 / 作废",
                Gate = "phase",
            };
        }

        return null;
    }

    /// <summary>触发型裁定点（如心上人死亡触发的说书人选择，R-0039）未了结。</summary>
    internal static CommandRejection? TriggerDecisionPending(StepMachineState? machine, GameCommand command) =>
        machine is { AwaitingDecision: not null, AwaitingDecisionTriggerAbility: not null }
        && IsAdvancing(command)
            ? new CommandRejection
            {
                Code = "phase.trigger_choice_pending",
                Message = "还有一条未了结的触发型裁定（说书人）：先裁定，或由说书人强推 / 收口",
                Gate = "phase",
            }
            : null;

    /// <summary>艺术家的白天提问（R-0040）未结清：只挡阶段推进 / 收口白天。</summary>
    internal static CommandRejection? ArtistQuestionPending(StepMachineState? machine, GameCommand command)
    {
        if (machine?.ArtistQuestion is null)
        {
            return null;
        }

        return command is StartPhaseCommand
            or StartDayCommand
            or StartNightCommand
            or CloseDayCommand
            or PunishExecutionCommand
            ? new CommandRejection
            {
                Code = "phase.artist_question_pending",
                Message = "还有一条未结清的艺术家提问（说书人）：先回答 / 要求重问，或由说书人强推作废",
                Gate = "phase",
            }
            : null;
    }

    /// <summary>「推进 / 收口类」命令的完整名单：与触发型挂起的既有口径一致。</summary>
    private static bool IsAdvancing(GameCommand command) =>
        command is StartPhaseCommand
            or StartDayCommand
            or StartNightCommand
            or NominateCommand
            or CastVoteCommand
            or StartVoteSweepCommand
            or CollectSeatVoteCommand
            or ResumeVoteSweepCommand
            or CountVotesCommand
            or ProposeExileCommand
            or CastExileVoteCommand
            or StartExileSweepCommand
            or CollectExileSeatVoteCommand
            or ResumeExileSweepCommand
            or CountExileVotesCommand
            or CloseDayCommand
            or PunishExecutionCommand;
}
