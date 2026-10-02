using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 一次提交的收尾助手：事件草案的序号编排与胜负求值。
/// </summary>
/// <remarks>
/// 从 <see cref="GameSession"/> 拆出（单文件 600 行门禁）：会话管"状态与提交"，
/// 这里只做纯函数的活——装草案、判胜负、收口结束批次。胜负判定的口径见
/// <c>docs/standard/rulings.md</c> R-0024（业务事件先判一次、触发之后统一再判）；
/// 结束批次的挂起请求作废见 <see cref="AppendGameEnding"/>。
/// </remarks>
internal static class SessionCommit
{
    /// <summary>把一批事件装进提交草案，返回新的末序号（序号连续、单调）。</summary>
    internal static long AppendDrafts(
        List<StoredEventDraft> drafts,
        IReadOnlyList<GameEvent> events,
        long lastSequence,
        DateTimeOffset recordedAt)
    {
        var sequence = lastSequence;
        foreach (var gameEvent in events)
        {
            sequence++;
            drafts.Add(new StoredEventDraft
            {
                Sequence = sequence,
                Event = gameEvent,
                RecordedAt = recordedAt,
            });
        }

        return sequence;
    }

    /// <summary>
    /// 补全角色变化的「变化前角色」（<c>docs/standard/rulings.md</c> R-0029）：用**提交前**的账，
    /// 给每条观测到角色的 <see cref="SeatStateChangedEvent"/> 填上该席位此前的角色。
    /// </summary>
    /// <remarks>
    /// 与「维度 → 效果链接」的补全同族：产出方（角色契约 / 说书人上报 / 开局分配）只报**新值**，
    /// 「从什么变成什么」由提交管线统一补齐——否则「恶魔 → 非恶魔」这个事实在账被覆盖后就丢了，
    /// 胜负求值只能看见"现在没有恶魔"，无法与"配置错误"区分（R-0024 第 4 条）。
    /// 产出方自己填过的值一律尊重（未来某条路径若自带历史，不被覆盖）。
    /// </remarks>
    internal static IReadOnlyList<GameEvent> FillPreviousCharacters(
        IReadOnlyList<GameEvent> events,
        GameState before)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(before);

        var filled = new List<GameEvent>(events.Count);
        foreach (var gameEvent in events)
        {
            if (gameEvent is SeatStateChangedEvent { Character: not null, PreviousCharacter: null } changed)
            {
                filled.Add(changed with { PreviousCharacter = before.Seat(changed.Seat)?.CharacterValue });
                continue;
            }

            filled.Add(gameEvent);
        }

        return filled;
    }

    /// <summary>
    /// 一次胜负求值（R-0024）：**对局开始前不判**（还没开过任何阶段时没有"胜负"这回事，
    /// 开局配置也不构成条件）；已经结束时不再判（避免重复结束事件）；
    /// 席位观测不齐的情形由求值器按"不猜"处理，这里不做任何默认补齐。
    /// </summary>
    internal static GameOutcome? EvaluateOutcome(
        GameSetup? setup,
        GameState state,
        StepMachineState? machine,
        IReadOnlyList<GameEvent> events)
    {
        if (machine is null || machine.Outcome is not null)
        {
            return null;
        }

        return OutcomeEvaluator.Evaluate(new OutcomeContext
        {
            State = state,
            Seats = setup is null
                ? []
                : [.. setup.Seats.Select(ticket => ticket.Seat).OrderBy(seat => seat.Value)],
            Characters = WinConditionFacts.Instance,
            Day = machine?.Day,
            Events = events,
        });
    }

    /// <summary>对账诊断落调试日志："本次没有重算"是输入不全时的诚实结论，不是错误。</summary>
    internal static void LogDiagnostics(IReadOnlyList<string> diagnostics, ILogger logger, GameId gameId)
    {
        foreach (var diagnostic in diagnostics)
        {
            logger.LogDebug(
                "结算对账本次未重算：{Diagnostic} game={GameId}",
                diagnostic,
                gameId);
        }
    }

    /// <summary>把一批派生事件追加进草案，并折进步骤机视图（账由对账内部折好，D-0010）。</summary>
    /// <param name="drafts">提交草案（就地追加）。</param>
    /// <param name="sink">本批派生事件清单（求值用；就地追加）。</param>
    /// <param name="events">要追加的派生事件。</param>
    /// <param name="sequence">当前末序号。</param>
    /// <param name="recordedAt">本批时间戳。</param>
    /// <param name="machine">步骤机状态（可能为 null = 还没开阶段）。</param>
    internal static (long Sequence, StepMachineState? Machine) AppendDerived(
        List<StoredEventDraft> drafts,
        List<GameEvent> sink,
        IReadOnlyList<GameEvent> events,
        long sequence,
        DateTimeOffset recordedAt,
        StepMachineState? machine)
    {
        foreach (var derived in events)
        {
            sequence++;
            drafts.Add(new StoredEventDraft
            {
                Sequence = sequence,
                Event = derived,
                RecordedAt = recordedAt,
            });
            sink.Add(derived);

            // 派生事件同样要折进**两个**派生视图：账由对账内部折好，步骤机状态在这里补折
            // （触发型请求例如呆瓜选择落在它上面）。
            machine = StepMachine.Apply(machine, derived) ?? machine;
        }

        return (sequence, machine);
    }

    /// <summary>
    /// 结束批次的收口：先把仍挂起的操作请求作废、把等待说书人的裁定点收口（若有），
    /// 再追加唯一的结束事件并把它们折进派生视图（R-0024；票据 ended-game-pending-request-void）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **顺序不能反**：作废事件排在 <see cref="GameEndedEvent"/> 之前同批落库，重放才读得出
    /// "请求已作废 + 本局已结束"。否则终局快照会永久携带一条答不了、也撤不掉的死信——
    /// 重连把它重投给玩家，而一切提交都被 <c>phase.game_ended</c> 拒（D-0010 / D-0011 / D-0014）。
    /// </para>
    /// <para>
    /// 两类挂起——操作请求（槽位来源与触发来源一视同仁）与等待说书人的裁定点——在结束之后
    /// 都没有任何出口能再了结它们，因此一并收口；没有挂起时不产生多余事件（幂等）。
    /// 收口不重跑触发管线（《处决》第 3 步先于第 4 步）。
    /// </para>
    /// </remarks>
    internal static (long Sequence, GameState State, StepMachineState? Machine) AppendGameEnding(
        GameOutcome outcome,
        List<StoredEventDraft> drafts,
        long sequence,
        DateTimeOffset recordedAt,
        GameState state,
        StepMachineState? machine,
        GameId gameId,
        ILogger logger)
    {
        if (machine?.PendingRequest is { Status: OperationRequestStatus.Pending } pending)
        {
            var voided = new OperationRequestVoidedEvent
            {
                RequestId = pending.Id,
                Void = new OperationRequestVoid
                {
                    Reason = OperationRequestVoidReason.GameEnded,
                    Note = $"本局已结束（{(outcome.Winner == Alignment.Good ? "善良" : "邪恶")}阵营获胜）："
                        + "请求不再有意义",
                },
            };
            sequence++;
            drafts.Add(new StoredEventDraft
            {
                Sequence = sequence,
                Event = voided,
                RecordedAt = recordedAt,
            });
            machine = StepMachine.Apply(machine, voided) ?? machine;

            logger.LogInformation(
                "结束批次作废挂起请求：game={GameId} request={RequestId} addressee={Seat} reason={Reason} 说明={Note}",
                gameId,
                pending.Id.Value,
                pending.Addressee.Value,
                OperationRequestVoidReason.GameEnded,
                voided.Void.Note);
        }

        // 与挂起请求同族的第二个挂起：等待说书人的裁定点。结束之后它同样再也不会被回答
        // （一切输入被 phase.game_ended 拒），以"本局已结束"收口，终局快照不留悬挂
        // （收口姿态同强推：Decision = null + 说明）。
        if (machine?.AwaitingDecision is { } decision)
        {
            var resolved = new DecisionPointResolvedEvent
            {
                DecisionPointId = decision.Id,
                Decision = null,
                Note = "本局已结束：裁定点不再有意义",
            };
            sequence++;
            drafts.Add(new StoredEventDraft
            {
                Sequence = sequence,
                Event = resolved,
                RecordedAt = recordedAt,
            });
            machine = StepMachine.Apply(machine, resolved) ?? machine;

            logger.LogInformation(
                "结束批次收口挂起裁定点：game={GameId} decisionPoint={DecisionPointId} 说明={Note}",
                gameId,
                decision.Id.Value,
                resolved.Note);
        }

        var ended = new GameEndedEvent
        {
            Winner = outcome.Winner,
            Condition = outcome.Condition,
            Detail = outcome.Detail,
        };
        sequence++;
        drafts.Add(new StoredEventDraft
        {
            Sequence = sequence,
            Event = ended,
            RecordedAt = recordedAt,
        });

        logger.LogInformation(
            "本局结束：game={GameId} winner={Winner} condition={Condition} detail={Detail}",
            gameId,
            outcome.Winner,
            outcome.Condition,
            outcome.Detail);

        return (sequence, GameStateMachine.Apply(state, ended), StepMachine.Apply(machine, ended) ?? machine);
    }

    /// <summary>重复投递按回执重放：读回首次提交落库的事件，返回同一份结果（D-0012 幂等闸）。</summary>
    internal static async Task<CommandResult> ReplayAsync(
        IGameStore store,
        GameId gameId,
        ILogger logger,
        CommandEnvelope envelope,
        CommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        var events = new List<GameEvent>();
        if (!receipt.IsEmpty)
        {
            var stored = await store.ReadEventsAsync(gameId, receipt.FirstSequence - 1, cancellationToken);
            events.AddRange(
                stored.Where(item => item.Sequence <= receipt.LastSequence).Select(item => item.Event));
        }

        logger.LogInformation(
            "重复投递按回执重放：game={GameId} key={Key} command={Command} 序号={First}..{Last} clientSequence={ClientSequence}",
            gameId,
            receipt.IdempotencyKey,
            envelope.Command.GetType().Name,
            receipt.FirstSequence,
            receipt.LastSequence,
            envelope.ClientSequence);

        return new CommandResult
        {
            Kind = CommandResultKind.Duplicate,
            Sequence = receipt.LastSequence,
            Events = events,
            Notifications = [],
        };
    }
}
