using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 命令提交管线：把分派结果（业务事件）落成「账 → 派生事件 → 胜负 → 原子提交 → 通知」。
/// </summary>
/// <remarks>
/// 从 <see cref="GameSession"/> 拆出（单文件 600 行门禁）：这里**不持有会话状态**——
/// 输入是当前账 / 步骤机 / 序号与分派结果，输出是下一份状态与回执材料；
/// 会话状态的写入由调用方在同一把锁内完成。
/// </remarks>
internal static class SessionPipeline
{
    /// <summary>执行一次提交，返回下一份状态、业务事件与通知。</summary>
    internal static async Task<CommitOutcome> CommitAsync(
        IGameStore store,
        GameId gameId,
        ILogger logger,
        CommandEnvelope envelope,
        CommandDispatchResult dispatch,
        GameSetup? setup,
        GameState state,
        StepMachineState? machine,
        SessionTrackers trackers,
        SettlementContext settlement,
        long lastSequence,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        // 补全「变化前角色」（R-0029）：产出方只报新值，由提交管线用**提交前**的账补齐——
        // 否则「恶魔 → 非恶魔」在账被覆盖后无从读出，胜负求值只能看见"现在没有恶魔"，
        // 与"从未配置恶魔"混为一谈（R-0024 第 4 条）。
        var businessEvents = SessionCommit.FillPreviousCharacters(dispatch.Events, state);
        var drafts = new List<StoredEventDraft>(businessEvents.Count);
        var sequence = SessionCommit.AppendDrafts(drafts, businessEvents, lastSequence, recordedAt);

        // 先把这一步的账在内存里折出来：折不动就整条命令失败，绝不落库。
        // 否则会留下"事件已落库、账没折"的中间态，而重投会被当成 Duplicate —— 分叉永远暴露不出来。
        var nextState = FoldLedger(state, drafts);
        var nextMachine = dispatch.Machine;
        var derivedEvents = new List<GameEvent>();

        // ① 先判一次（《处决》一些相关效果的触发时机第 3 步先于第 4 步）：
        //    处决这一批如果本身已经满足胜负条件，就不再结算死亡触发能力（呆瓜不需要再选择）。
        var outcome = SessionCommit.EvaluateOutcome(setup, nextState, nextMachine, businessEvents);

        if (outcome is null)
        {
            // 固定点对账：事件触发（女巫 / 呆瓜等）→ 常驻效果 / 能力存续 / 维度重算（D-0015 推论 1）。
            // 派生事件与业务事件**同一次提交**落库；重放只折事件，恢复不重算。
            var reconciliation = SessionSettlement.Reconcile(
                nextState,
                settlement with { Machine = nextMachine },
                businessEvents);
            SessionCommit.LogDiagnostics(reconciliation.Diagnostics, logger, gameId);

            // 派生事件入账 + 裁定结清后的续推（H-1：触发型 / 触发格裁定结清且无后续挂起时
            // 重进本格复位配额；内核只落裁定本身、不产推进事件）。
            (sequence, nextMachine) = SessionCommit.AppendDerivedWithContinuation(
                drafts,
                derivedEvents,
                businessEvents,
                reconciliation.Events,
                sequence,
                recordedAt,
                nextMachine);
            nextState = reconciliation.State;

            // ② 事务提交后统一判定（R-0008）：触发产出的死亡同样可能满足胜负条件。
            outcome = SessionCommit.EvaluateOutcome(setup, nextState, nextMachine, derivedEvents);
        }
        else
        {
            // 游戏已经结束：跳过事件触发（第 3 步先于第 4 步），但仍做完账实一致的收尾——
            // 它不产生规则后果，只保证终局的账与效果链不自相矛盾。
            var housekeeping = SessionSettlement.ReconcileHousekeeping(
                nextState,
                settlement with { Machine = nextMachine });
            SessionCommit.LogDiagnostics(housekeeping.Diagnostics, logger, gameId);
            (sequence, nextMachine) = SessionCommit.AppendDerived(
                drafts,
                derivedEvents,
                housekeeping.Events,
                sequence,
                recordedAt,
                nextMachine);
            nextState = housekeeping.State;
        }

        if (outcome is not null)
        {
            // 结束批次收口：先作废仍挂起的请求（若有），再追加唯一的结束事件（R-0024）。
            (sequence, nextState, nextMachine) = SessionCommit.AppendGameEnding(
                outcome,
                drafts,
                sequence,
                recordedAt,
                nextState,
                nextMachine,
                gameId,
                logger);
        }

        await store.CommitAsync(
            new GameCommit
            {
                GameId = gameId,
                Events = drafts,
                Snapshot = new StoredSnapshot
                {
                    Sequence = sequence,
                    Machine = nextMachine,
                    RecordedAt = recordedAt,
                },
                Receipt = new CommandReceipt
                {
                    IdempotencyKey = envelope.IdempotencyKey,
                    FirstSequence = lastSequence + 1,
                    LastSequence = sequence,
                },
            },
            cancellationToken);

        var publicSurfaceChanged = trackers.Update(drafts, recordedAt);
        var notifications = GameNotificationBuilder.Build(drafts, machine, publicSurfaceChanged);
        logger.LogInformation(
            "命令已接受：game={GameId} actor={ActorKind} command={Command} 事件数={EventCount} 派生事件数={DerivedCount} 序号={Sequence} 挂起={Held} clientSequence={ClientSequence}",
            gameId,
            envelope.Actor.Kind,
            envelope.Command.GetType().Name,
            businessEvents.Count,
            derivedEvents.Count,
            sequence,
            nextMachine?.IsHeld,
            envelope.ClientSequence);

        return new CommitOutcome
        {
            Machine = nextMachine,
            State = nextState,
            Sequence = sequence,
            Events = businessEvents,
            Notifications = notifications,
        };
    }

    /// <summary>
    /// 把一批事件折进状态账的**副本**（与步骤机状态同源、同一批事件）。
    /// </summary>
    /// <remarks>
    /// 刻意不改调用方持有的账：先在内存里折成功，才允许把事件提交落库，提交成功后才把结果赋回去。
    /// 这样"折不动"等价于"这条命令失败"，不会出现半提交的账。
    /// </remarks>
    private static GameState FoldLedger(GameState state, IReadOnlyList<StoredEventDraft> drafts)
    {
        var next = state;
        foreach (var draft in drafts)
        {
            next = GameStateMachine.Apply(next, draft.Event);
        }

        return next;
    }

    /// <summary>提交管线算出的下一份状态与回执材料。</summary>
    internal sealed record CommitOutcome
    {
        /// <summary>提交后的步骤机状态。</summary>
        public required StepMachineState? Machine { get; init; }

        /// <summary>提交后的状态账。</summary>
        public required GameState State { get; init; }

        /// <summary>提交后的最新事件序号。</summary>
        public required long Sequence { get; init; }

        /// <summary>本次的业务事件（用于回执与审计；派生事件只进事件流）。</summary>
        public required IReadOnlyList<GameEvent> Events { get; init; }

        /// <summary>本次要推送的通知。</summary>
        public required IReadOnlyList<GameNotification> Notifications { get; init; }
    }
}
