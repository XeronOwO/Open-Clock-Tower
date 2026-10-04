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
    /// 分派一条命令；步骤机尚未开启时，只有开始阶段 / 开局分配 / <b>状态观测</b> / 开夜 / <b>注记</b>
    /// 这些命令会走到这里。
    /// <paramref name="settlement"/> 携带当前账、座次与结算契约目录——行动槽位结算要靠它。
    /// <paramref name="annotations"/> 是当前注记账（D-0019）：注记的签发标识与改 / 删目标按它定位。
    /// </summary>
    internal static CommandDispatchResult Dispatch(
        CommandEnvelope envelope,
        StepMachineState? machine,
        GameSetup? setup,
        SeatAnnotationLedger annotations,
        SettlementContext settlement,
        GameId gameId,
        ILogger logger)
    {
        if (envelope.Command is StartPhaseCommand start)
        {
            var outcome = StepMachine.StartPhase(start.Plan, machine, settlement.State, start.Control);
            return new CommandDispatchResult(outcome.State, outcome.Events, null);
        }

        if (envelope.Command is AssignCharactersCommand assign)
        {
            return DispatchAssignCharacters(assign, setup, settlement.State);
        }

        if (envelope.Command is ApplySeatStateCommand applySeatState && machine is null)
        {
            // 预阶段的状态观测：状态账本来就是"观测即记账"（D-0015），开局分配走的也是这条通路（D-0017）。
            // 注意：建表要求每一席都有角色（plan.seat_unassigned），所以这里不是"补角色"的旁路。
            return DispatchPrePhaseSeatState(applySeatState);
        }

        if (envelope.Command is AddSeatAnnotationCommand or UpdateSeatAnnotationCommand or RemoveSeatAnnotationCommand)
        {
            // 注记不参与阶段推进：首个阶段之前也能写（与状态观测同一姿态，D-0019）。
            return AnnotationCommandDispatch.Dispatch(envelope.Command, machine, annotations);
        }

        // 旅行者加入 / 离场（D1）：任意时刻可用，不能落到下面的 kernel.not_started。
        if (envelope.Command is JoinTravellerCommand or RemoveTravellerCommand)
        {
            return setup is null ? MissingSetup() : TravellerCommandDispatch.Dispatch(envelope.Command, machine, setup, settlement.State, gameId, logger);
        }

        if (envelope.Command is StartNightCommand startNight)
        {
            return DispatchStartNight(startNight, machine, setup, settlement.State, gameId, logger);
        }

        if (envelope.Command is StartDayCommand)
        {
            if (machine is null)
            {
                return CommandDispatchResult.Rejected(new CommandRejection
                {
                    Code = "phase.day_requires_night",
                    Message = "白天只能跟在夜晚之后：本局还没有开始过任何阶段",
                    Gate = "phase",
                });
            }

            return DispatchStartDay(machine, setup, settlement.State, gameId, logger);
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

        // 白天玩家命令要走带行动者席位的形状转换（提名者 / 投票者 / 流放发起者 / 流放表决者
        // 都来自凭据推导，命令面无自称身份）。
        if (envelope.Command is NominateCommand or CastVoteCommand or ProposeExileCommand or CastExileVoteCommand)
        {
            // 结构上不依赖闸门顺序：拿不到席位就在这里显式拒绝，而不是靠 `Seat!` 之后的空引用崩溃。
            if (envelope.Actor.Seat is not { } actor)
            {
                return CommandDispatchResult.Rejected(new CommandRejection
                {
                    Code = "identity.player_only",
                    Message = "白天的玩家命令必须由持席位的玩家发出",
                    Gate = "identity",
                });
            }

            return Translate(StepMachine.Handle(machine, settlement, BuildPlayerDayInput(envelope.Command, actor)));
        }

        // 艺术家的白天提问（R-0040）：席位从凭据推导（命令面无自称身份），问题原样交给内核校验。
        if (envelope.Command is AskArtistQuestionCommand askQuestion)
        {
            if (envelope.Actor.Seat is not { } artist)
            {
                return CommandDispatchResult.Rejected(new CommandRejection
                {
                    Code = "identity.player_only",
                    Message = "艺术家的提问必须由持席位的玩家发出",
                    Gate = "identity",
                });
            }

            return Translate(StepMachine.Handle(machine, settlement, new AskArtistQuestionInput
            {
                Seat = artist,
                Question = askQuestion.Question,
            }));
        }

        if (envelope.Command is StartVoteSweepCommand or ResumeVoteSweepCommand or CountVotesCommand
            or StartExileSweepCommand or ResumeExileSweepCommand or CountExileVotesCommand
            or ResolveDayProtectionCommand
            or CloseDayCommand)
        {
            return Translate(StepMachine.Handle(machine, settlement, BuildStorytellerDayInput(envelope.Command)));
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

        var handled = StepMachine.Handle(machine, settlement, input);

        // 说书人手工上报「角色变化」也走同一套槽位绑定（R-0032）：新角色今夜尚未进入的槽位
        // 跟随它激活 / 换手重绑，已经进入过的槽位不动。角色契约（结算时）之外，这是唯一的另一条入口。
        if (envelope.Command is ApplySeatStateCommand seatChange)
        {
            handled = WithManualCharacterSlotBinding(handled, machine, settlement, seatChange);
        }

        return Translate(handled);
    }

    /// <summary>
    /// 说书人手工上报角色变化 → 把新角色今夜尚未进入的槽位接到同一套绑定上（R-0032）：
    /// 空槽位激活、行动槽位换手重绑；已经进入过的槽位不处理（过时不候）。
    /// 与角色契约（麻脸巫婆 / 舞蛇人）共用 <see cref="NightSlotActivation"/>，避免两条入口两套口径。
    /// </summary>
    private static StepMachineOutcome WithManualCharacterSlotBinding(
        StepMachineOutcome outcome,
        StepMachineState machine,
        SettlementContext settlement,
        ApplySeatStateCommand command)
    {
        if (outcome.Kind != StepMachineOutcomeKind.Applied || command.Character is not { } character)
        {
            return outcome;
        }

        // 折出上报后的账：槽位提示按新账构建（与「结算时按当前账求值」同一姿态）。
        var after = settlement.State;
        foreach (var gameEvent in outcome.Events)
        {
            after = GameStateMachine.Apply(after, gameEvent);
        }

        var binding = NightSlotActivation.Plan(
            machine.Plan,
            machine.SlotIndex,
            command.Seat,
            character,
            after,
            machine.Day?.Days.LastOrDefault(),
            settlement.Seats,
            NightActions.Default);
        if (binding is null)
        {
            return outcome;
        }

        // 绑定事件要折进步骤机状态（与 StepMachine.Applied 的折法一致）：它改的是计划里那一格。
        var bound = StepMachine.Apply(outcome.State, binding)
            ?? throw new InvalidOperationException("事件流损坏：槽位绑定后丢失步骤机状态");

        return outcome with
        {
            State = bound,
            Events = [.. outcome.Events, binding],
        };
    }

    /// <summary>把内核结果翻译成命令结果：拒绝码优先用内核给出的（白天规则的机器可读码），否则按枚举生成。</summary>
    private static CommandDispatchResult Translate(StepMachineOutcome outcome)
    {
        if (outcome.Kind == StepMachineOutcomeKind.Rejected)
        {
            return CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = outcome.RejectionCode ?? $"kernel.{outcome.RejectionReason}",
                Message = outcome.RejectionNote ?? "内核拒绝了这条输入",
                Gate = "kernel",
            });
        }

        return new CommandDispatchResult(outcome.State, outcome.Events, null);
    }

    /// <summary>玩家白天命令 → 内核输入（行动者席位由调用方从凭据推导后传入，非空）。</summary>
    private static StepMachineInput BuildPlayerDayInput(GameCommand command, SeatId actor) =>
        command switch
        {
            NominateCommand nominate => new NominateInput
            {
                Nominator = actor,
                Nominee = nominate.Nominee,
            },
            CastVoteCommand castVote => new CastVoteInput
            {
                Voter = actor,
                NominationIndex = castVote.NominationIndex,
                Voted = castVote.Voted,
            },
            ProposeExileCommand proposeExile => new ProposeExileInput
            {
                Proposer = actor,
                Target = proposeExile.Target,
            },
            CastExileVoteCommand castExile => new CastExileVoteInput
            {
                Voter = actor,
                ExileIndex = castExile.ExileIndex,
                Voted = castExile.Voted,
            },
            _ => throw new InvalidOperationException($"不是玩家白天命令：{command.GetType().Name}"),
        };

    /// <summary>说书人白天命令 → 内核输入（收票 / 计票与结束白天不携带行动者席位）。</summary>
    private static StepMachineInput BuildStorytellerDayInput(GameCommand command) =>
        command switch
        {
            StartVoteSweepCommand startSweep => new StartVoteSweepInput
            {
                NominationIndex = startSweep.NominationIndex,
                CountdownMilliseconds = startSweep.CountdownMilliseconds,
                IntervalMilliseconds = startSweep.IntervalMilliseconds,
            },
            ResumeVoteSweepCommand resumeSweep => new ResumeVoteSweepInput
            {
                NominationIndex = resumeSweep.NominationIndex,
            },
            CountVotesCommand countVotes => new CountVotesInput
            {
                NominationIndex = countVotes.NominationIndex,
            },
            StartExileSweepCommand startExile => new StartExileSweepInput
            {
                ExileIndex = startExile.ExileIndex,
                CountdownMilliseconds = startExile.CountdownMilliseconds,
                IntervalMilliseconds = startExile.IntervalMilliseconds,
            },
            ResumeExileSweepCommand resumeExile => new ResumeExileSweepInput
            {
                ExileIndex = resumeExile.ExileIndex,
            },
            CountExileVotesCommand countExile => new CountExileVotesInput
            {
                ExileIndex = countExile.ExileIndex,
            },
            ResolveDayProtectionCommand resolveProtection => new ResolveDayProtectionInput
            {
                Seat = resolveProtection.Seat,
                Protected = resolveProtection.Protected,
                Note = resolveProtection.Note,
            },
            CloseDayCommand => new CloseDayInput(),
            _ => throw new InvalidOperationException($"不是说书人白天命令：{command.GetType().Name}"),
        };

    /// <summary>
    /// 开白天：校验座位角色齐全，并对"与白天相关但契约未实现"的角色显式拒绝（不静默跳过）；
    /// 通过后按天数构造唯一的 DayWindow 计划，交给步骤机。
    /// </summary>
    private static CommandDispatchResult DispatchStartDay(
        StepMachineState machine,
        GameSetup? setup,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (setup is null)
        {
            return MissingSetup();
        }

        var seats = InGameSeats.Derive(setup, state);

        foreach (var seat in seats)
        {
            var character = state.Seat(seat)?.CharacterValue;
            if (character is null)
            {
                return CommandDispatchResult.Rejected(new CommandRejection
                {
                    Code = "legality.character_unobserved",
                    Message = $"席位 {seat.Value} 的角色还没有观测：无法核对白天契约（不猜）",
                    Gate = "legality",
                });
            }

            if (DayActions.IsDayRelevant(character.Value) && !DayActions.IsCovered(character.Value))
            {
                return CommandDispatchResult.Rejected(new CommandRejection
                {
                    Code = "legality.day_contract_missing",
                    Message = $"角色 {character.Value.Value} 的白天相关能力还没有实现：按「不静默跳过」拒绝开白天",
                    Gate = "legality",
                });
            }
        }

        var dayNumber = (machine.Day?.Days.Count ?? 0) + 1;
        var plan = new StepPlan
        {
            Label = $"sv:day-{dayNumber}",
            Phase = GamePhase.Day,
            Slots = [StepSlot.DayWindow(new StepSlotId("day-window"))],
        };

        var started = StepMachine.StartDay(plan, dayNumber, machine);
        logger.LogInformation(
            "已开启白天：game={GameId} day={DayNumber} 计划={Label} 席位数={SeatCount}",
            gameId,
            dayNumber,
            plan.Label,
            seats.Count);

        return new CommandDispatchResult(started.State, started.Events, null);
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
        // 任意时间点玩家必居二者之一，而开局没有任何醉酒 / 中毒来源）；初始阵营 = 角色类型对应阵营（R-0023，
        // 依据《术语汇总》：镇民 / 外来者初始为善良，爪牙 / 恶魔初始为邪恶）。
        // 这是补全初始条件，不是运行期把维度耦合在一起：运行期的状态观测仍是一次只报本次观测到的维度。
        var events = command.Assignments
            .OrderBy(assignment => assignment.Seat.Value)
            .Select(assignment => (GameEvent)new SeatStateChangedEvent
            {
                Seat = assignment.Seat,
                Character = assignment.Character,
                Alignment = InitialAlignmentOf(assignment.Character),
                Life = LifeState.Alive,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = SetupAssignmentReason,
            })
            .ToArray();

        return new CommandDispatchResult(null, events, null);
    }

    /// <summary>初始阵营 = 角色类型对应阵营：镇民 / 外来者 → 善良，爪牙 / 恶魔 → 邪恶（R-0023）。</summary>
    /// <exception cref="InvalidOperationException">
    /// 角色不在首版花名册里，或是类型无法推导阵营的旅行者（合法性闸本应拦下，不许静默）。
    /// </exception>
    private static Alignment InitialAlignmentOf(CharacterId character) =>
        SectsAndVioletsRoster.TypeOf(character) switch
        {
            CharacterType.Townsfolk or CharacterType.Outsider => Alignment.Good,
            CharacterType.Minion or CharacterType.Demon => Alignment.Evil,
            _ => throw new InvalidOperationException(
                $"角色 {character.Value} 不是四类型之一（旅行者阵营由说书人私下裁定），无法推导初始阵营（合法性闸本应拦下）"),
        };

    /// <summary>
    /// 预阶段观位状态观测：只写"本次观测到的维度"这一条账事件，不动步骤机（它还没有状态）。
    /// </summary>
    /// <remarks>
    /// 为什么需要它：夜晚建表要求**每一席的生死都已观测**，否则整条开夜命令被拒绝；
    /// 而分配只覆盖被分配到的席位（未分配的席位必须能补报），所以观测必须能在
    /// 首个阶段开始前写入。事件形状与运行期完全一致（同样带原因与归因），重放不做特殊处理。
    /// </remarks>
    private static CommandDispatchResult DispatchPrePhaseSeatState(ApplySeatStateCommand command)
    {
        if (command.Life is null
            && command.Character is null
            && command.Alignment is null
            && command.Drunk is null
            && command.Poison is null)
        {
            return CommandDispatchResult.Rejected(new CommandRejection
            {
                Code = "legality.seat_state_empty",
                Message = "座位状态变化至少要给出一个观测维度",
                Gate = "legality",
            });
        }

        var events = new GameEvent[]
        {
            new SeatStateChangedEvent
            {
                Seat = command.Seat,
                Life = command.Life,
                Character = command.Character,
                Alignment = command.Alignment,
                Drunk = command.Drunk,
                Poison = command.Poison,
                Reason = command.Reason,
                CausedBy = command.CausedBy,
            },
        };

        return new CommandDispatchResult(null, events, null);
    }


    /// <summary>开夜：用会话席位名单 + 当前状态账按规则表建表，然后交给步骤机开启阶段。</summary>
    /// <remarks>
    /// 建表失败（席位缺角色 / 生死未观测 / 契约未实现）整条命令被拒绝——
    /// 宁可开不了夜，也不静默给出半张表。
    /// </remarks>
    private static CommandDispatchResult DispatchStartNight(
        StartNightCommand command,
        StepMachineState? machine,
        GameSetup? setup,
        GameState state,
        GameId gameId,
        ILogger logger)
    {
        if (setup is null)
        {
            return MissingSetup();
        }

        var seats = InGameSeats.Derive(setup, state);
        var outcome = NightPlanBuilder.Build(new NightPlanRequest
        {
            NightNumber = command.NightNumber,
            Variant = command.Variant,
            Seats = seats,
            State = state,
            LastDay = machine?.Day?.Days.LastOrDefault(),
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

        var started = StepMachine.StartPhase(outcome.Plan, machine, state);
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
