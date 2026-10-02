using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 一次提交的收尾助手：事件草案的序号编排与胜负求值。
/// </summary>
/// <remarks>
/// 从 <see cref="GameSession"/> 拆出（单文件 600 行门禁）：会话管"状态与提交"，
/// 这里只回答两件纯函数的活——装草案、判胜负。胜负判定的口径见
/// <c>docs/standard/rulings.md</c> R-0024（业务事件先判一次、触发之后统一再判）。
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

    /// <summary>追加唯一的结束事件，并把结论折进两个派生视图（R-0024）。</summary>
    internal static (long Sequence, GameState State, StepMachineState? Machine) AppendGameEnded(
        GameOutcome outcome,
        List<StoredEventDraft> drafts,
        long sequence,
        DateTimeOffset recordedAt,
        GameState state,
        StepMachineState? machine)
    {
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
