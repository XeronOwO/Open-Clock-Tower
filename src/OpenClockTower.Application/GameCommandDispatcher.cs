using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 命令 → 内核产出的分派：步骤机输入、开局分配（阶段前的账事件）、开夜（建表后开阶段）。
/// </summary>
/// <remarks>
/// 从 <see cref="GameSession"/> 拆出：会话管"状态与提交"，这里只回答"这条命令产出什么"。
/// 分派本身无副作用（只有日志）；命令是否被允许由 <see cref="CommandGatePipeline"/> 先判。
/// </remarks>
internal static class GameCommandDispatcher
{
    /// <summary>开局分配事件的固定原因：机器可读，便于在事件流里筛出"设置"而不是"对局中的变化"。</summary>
    private const string SetupAssignmentReason = "setup.assignment";

    /// <summary>
    /// 分派一条命令；步骤机尚未开启时，只有开始阶段 / 开局分配 / 开夜三类命令会走到这里。
    /// <paramref name="settlement"/> 携带当前账、座次与结算契约目录——行动槽位结算要靠它。
    /// </summary>
    internal static CommandDispatchResult Dispatch(
        CommandEnvelope envelope,
        StepMachineState? machine,
        GameSetup? setup,
        SettlementContext settlement,
        GameId gameId,
        ILogger logger)
    {
        if (envelope.Command is StartPhaseCommand start)
        {
            var outcome = StepMachine.StartPhase(start.Plan, start.Control);
            return new CommandDispatchResult(outcome.State, outcome.Events, null);
        }

        if (envelope.Command is AssignCharactersCommand assign)
        {
            return DispatchAssignCharacters(assign, setup, settlement.State);
        }

        if (envelope.Command is StartNightCommand startNight)
        {
            return DispatchStartNight(startNight, setup, settlement.State, gameId, logger);
        }

        if (machine is null)
        {
            return CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = "kernel.not_started",
                Message = "步骤机尚未开启任何阶段",
                Gate = "kernel",
            });
        }

        var input = KernelInputMapper.ToInput(envelope.Command);
        if (input is null)
        {
            return CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = "kernel.unsupported",
                Message = $"未支持的命令：{envelope.Command.GetType().Name}",
                Gate = "kernel",
            });
        }

        var outcome2 = StepMachine.Handle(machine, settlement, input);
        if (outcome2.Kind == StepMachineOutcomeKind.Rejected)
        {
            return CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = $"kernel.{outcome2.RejectionReason}",
                Message = outcome2.RejectionNote ?? "内核拒绝了这条输入",
                Gate = "kernel",
            });
        }

        return new CommandDispatchResult(outcome2.State, outcome2.Events, null);
    }

    /// <summary>开局分配：每席记录角色与初始生死（存活），产出一条 <see cref="SeatStateChangedEvent"/>。</summary>
    /// <remarks>
    /// 只允许在首个阶段开始前（阶段闸保证）；事件按席位号排序，同一批分配不受客户端排列影响（D-0008）。
    /// 初始生死的依据与"为什么这不是维度耦合"见 <see cref="AssignCharactersCommand"/> 与 rulings.md R-0015。
    /// </remarks>
    private static CommandDispatchResult DispatchAssignCharacters(
        AssignCharactersCommand command,
        GameSetup? setup,
        GameState state)
    {
        if (setup is null)
        {
            return MissingSetup();
        }

        // 角色唯一是全局不变量：分配闸管同批重复，这里查已进账的既有角色——
        // 否则跨批重复要拖到开夜才暴露，角色不在夜晚顺序表时甚至永远不暴露。
        foreach (var assignment in command.Assignments)
        {
            var holder = state.Seats.FirstOrDefault(entry =>
                entry.Seat != assignment.Seat && entry.CharacterValue == assignment.Character);
            if (holder is not null)
            {
                return CommandDispatchResult.Rejected(new CommandRejection
                {
                    Code = "legality.character_duplicated",
                    Message = $"角色 {assignment.Character.Value} 已经属于席位 {holder.Seat.Value}；角色唯一",
                    Gate = "legality",
                });
            }
        }

        // 初始生死 = 存活（R-0015）；初始醉酒 = 清醒、中毒 = 健康（R-0016，依据《重要细节》三-3：
        // 任意时间点玩家必居二者之一，而开局没有任何醉酒 / 中毒来源）。这是补全初始条件，
        // 不是运行期把维度耦合在一起：运行期的状态观测仍是一次只报本次观测到的维度。
        var events = command.Assignments
            .OrderBy(assignment => assignment.Seat.Value)
            .Select(assignment => (GameEvent)new SeatStateChangedEvent
            {
                Seat = assignment.Seat,
                Character = assignment.Character,
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = SetupAssignmentReason,
            })
            .ToArray();

        return new CommandDispatchResult(null, events, null);
    }

    /// <summary>开夜：用会话席位名单 + 当前状态账按规则表建表，然后交给步骤机开启阶段。</summary>
    /// <remarks>
    /// 建表失败（席位缺角色 / 生死未观测 / 契约未实现）整条命令被拒绝——
    /// 宁可开不了夜，也不静默给出半张表。
    /// </remarks>
    private static CommandDispatchResult DispatchStartNight(
        StartNightCommand command,
        GameSetup? setup,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (setup is null)
        {
            return MissingSetup();
        }

        var seats = setup.Seats.Select(item => item.Seat).OrderBy(seat => seat.Value).ToArray();
        var outcome = NightPlanBuilder.Build(new NightPlanRequest
        {
            NightNumber = command.NightNumber,
            Variant = command.Variant,
            Seats = seats,
            State = state,
            Actions = NightActions.Default,
        });

        if (outcome.Plan is null)
        {
            return CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = $"legality.{outcome.FailureCode}",
                Message = outcome.FailureMessage ?? "建表失败",
                Gate = "legality",
            });
        }

        var started = StepMachine.StartPhase(outcome.Plan);
        logger.LogInformation(
            "已按规则表建表：game={GameId} night={NightNumber} variant={Variant} 槽位数={SlotCount} 计划={Label}",
            gameId,
            command.NightNumber,
            command.Variant,
            outcome.Plan.Slots.Count,
            outcome.Plan.Label);

        return new CommandDispatchResult(started.State, started.Events, null);
    }

    private static CommandDispatchResult MissingSetup() =>
        CommandDispatchResult.Rejected(new CommandRejection
        {
            Code = "legality.setup_missing",
            Message = "本局还没有会话信息（席位名单）",
            Gate = "legality",
        });
}
