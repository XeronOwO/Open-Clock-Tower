using Microsoft.Extensions.Logging;
using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 一局游戏的状态持有者与用例编排：命令 → 四道闸 → 内核 → 事件原子落库 → 投影 / 通知。
/// </summary>
/// <remarks>
/// <para>
/// **状态属于持有者**：步骤机状态只在 <see cref="GameSession"/> 内部持有，对外只暴露窄接口
/// （命令、投影、重连包）。所有命令在同一把锁下串行执行，保证事件序号与状态一致。
/// </para>
/// <para>
/// **时间只能来自 <see cref="IClock"/>**：事件时间戳、卡点时长、节拍判定都在这一层；
/// 内核完全不接触时间（D-0008）。
/// </para>
/// <para>
/// **事件是唯一事实来源**（D-0010）：提交 = 事件 + 快照 + 回执的原子写入；恢复 = 全量重放；
/// 重建 = 重放后替换状态并显式报告是否一致（D-0014 能力 3）。
/// </para>
/// </remarks>
public sealed class GameSession
{
    private readonly IGameStore _store;
    private readonly IGameCatalog _catalog;
    private readonly IAbilityResolutionCatalog _abilities;
    private readonly IReadOnlyList<IStandingEffectSource> _standingEffects;
    private readonly IClock _clock;
    private readonly PacingOptions _pacing;
    private readonly ILogger<GameSession> _logger;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    /// <summary>会话级派生跟踪器：卡点起算、槽位起算、最近状态变化、结算结论与信息结果。</summary>
    private readonly SessionTrackers _trackers = new();

    private StepMachineState? _machine;

    /// <summary>服务端持有的会话信息（席位名单）；读一次后缓存（记录不可变）。</summary>
    private GameSetup? _setup;

    /// <summary>状态账（五个可观测维度的已知态 + 效果归因）。与步骤机同源折叠，见 <see cref="GameStateMachine"/>。</summary>
    private GameState _state = GameState.Empty;

    /// <summary>房间健康位：恢复 / 重建失败后为降级态，显式重建成功才清除（票据 room-health-degradation-flag）。</summary>
    private RoomHealth _health = RoomHealth.Healthy;

    private long _lastSequence;

    /// <summary>构造一局的编排器。</summary>
    /// <remarks>
    /// 依赖会话目录（<see cref="IGameCatalog"/>）只为开局分配与开夜：这两条命令需要**服务端持有的
    /// 席位名单**（客户端送来的席位声明不可信，D-0012）；其余命令不读目录。
    /// </remarks>
    public GameSession(
        GameId gameId,
        IGameStore store,
        IGameCatalog catalog,
        IAbilityResolutionCatalog abilities,
        IReadOnlyList<IStandingEffectSource> standingEffects,
        IClock clock,
        PacingOptions pacing,
        ILogger<GameSession> logger)
    {
        GameId = gameId;
        _store = store;
        _catalog = catalog;
        _abilities = abilities;
        _standingEffects = standingEffects;
        _clock = clock;
        _pacing = pacing;
        _logger = logger;
    }

    /// <summary>本局标识。</summary>
    public GameId GameId { get; }

    /// <summary>从事件流恢复全部状态（服务端启动 / 重启后的唯一正确入口）。</summary>
    /// <exception cref="InvalidOperationException">事件流损坏时显式抛出，绝不静默继续。</exception>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var storedEvents = await _store.ReadEventsAsync(GameId, afterSequence: 0, cancellationToken);
                StepMachineState? machine = null;
                var state = GameState.Empty;
                var lastSequence = 0L;

                foreach (var stored in storedEvents)
                {
                    machine = StepMachine.Apply(machine, stored.Event);
                    state = GameStateMachine.Apply(state, stored.Event);
                    lastSequence = stored.Sequence;
                }

                _machine = machine;
                _state = state;
                _lastSequence = lastSequence;
                _trackers.Recover(storedEvents, machine);
                _health = RoomHealth.Healthy;

                _logger.LogInformation(
                    "步骤机状态已从事件流恢复：game={GameId} 事件数={EventCount} 序号={Sequence} 槽位={SlotIndex} 挂起={Held}",
                    GameId,
                    storedEvents.Count,
                    _lastSequence,
                    _machine?.SlotIndex,
                    _machine?.IsHeld);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 事件流损坏或恢复失败：停在空状态，但保留序号连续性——新事件不得与库里旧行的主键冲突。
                // 刻意不放过任何异常：宁可让这一局显式不可用（等说书人重建），
                // 也不能把上一次的旧状态继续当成现状对外服务。
                _machine = null;
                _state = GameState.Empty;
                _lastSequence = await _store.FindLastSequenceAsync(GameId, cancellationToken);
                _trackers.Clear();
                _health = _health.Degrade($"恢复失败：{exception.Message}", _clock.UtcNow);
                _logger.LogError(
                    exception,
                    "恢复失败，房间健康位降级（等显式重建才清除）：game={GameId} 原因={Reason} 序号={Sequence}",
                    GameId,
                    _health.Reason,
                    _lastSequence);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>处理一条命令（同一局串行）。</summary>
    public async Task<CommandResult> ExecuteAsync(CommandEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteCoreAsync(envelope, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 节拍器心跳：配额到点就把它翻译成一条系统命令（时间 → 输入，D-0008）。
    /// </summary>
    /// <remarks>
    /// 接管模式下不做任何自动推进（D-0014 能力 2）；没有起点信息（异常数据）时宁可不动，
    /// 等说书人重建或强推。**返回本次心跳的提交结果**（没有到点时为 null）——
    /// 调用方拿到结果后必须照常分发通知，否则这一步产生的操作请求只会留在服务端。
    /// </remarks>
    public async Task<CommandResult?> TickAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var machine = _machine;
            if (machine is null || machine.IsPlanCompleted || machine.Outcome is not null)
            {
                // 计划走完 / 本局已结束：节拍器没有可以推进的槽位（R-0024）。
                return null;
            }

            if (machine.Control != ControlMode.Automatic || machine.Quota != SlotQuotaState.Running)
            {
                return null;
            }

            if (_trackers.SlotStartedAt is not { } startedAt || _clock.UtcNow < startedAt + _pacing.SlotQuota)
            {
                return null;
            }

            if (machine.CurrentSlot is { Kind: StepSlotKind.DayWindow })
            {
                // 白天窗口不消耗配额：白天节奏由说书人掌握，没有节拍可送。
                return null;
            }

            var slot = machine.CurrentSlot!;
            var envelope = new CommandEnvelope
            {
                Command = new SlotQuotaElapsedCommand(),
                Actor = Actor.System,
                IdempotencyKey = $"slot-elapsed:{machine.Plan.Label}:{slot.Id}",
            };
            return await ExecuteCoreAsync(envelope, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取某个玩家的投影（信息隔离在这里强制）。</summary>
    public PlayerView GetPlayerView(SeatId seat)
    {
        _gate.Wait();
        try
        {
            return GameProjection.ForSeat(
                _machine,
                _state,
                SeatList(),
                _lastSequence,
                seat,
                _trackers);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取说书人视图（含卡点、状态变化归因、每步摘要与兜底所需的一切）。</summary>
    public StorytellerView GetStorytellerView()
    {
        _gate.Wait();
        try
        {
            return StorytellerViewBuilder.Build(
                _machine,
                _state,
                _health,
                _lastSequence,
                _trackers,
                _clock.UtcNow,
                _abilities);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取重连包：投影 + **按接收者投影后**的补齐事件（绝不整条下发）。</summary>
    public async Task<ReconnectBundle> GetReconnectBundleAsync(
        SeatId seat,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        // 与其它视图读取同一把锁：重连包是"快照 + 补齐"的同一份事实，
        // 不加锁会在并发提交时把新序号与旧事件拼在一起（D-0010 的反面教材）。
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await SessionQueries.ReconnectBundleAsync(
                _store,
                GameId,
                _machine,
                _state,
                SeatList(),
                _lastSequence,
                _trackers,
                seat,
                Math.Clamp(afterSequence, 0, _lastSequence),
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<CommandResult> ExecuteCoreAsync(CommandEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _store.FindReceiptAsync(GameId, envelope.IdempotencyKey, cancellationToken);
            var setup = await EnsureSetupAsync(cancellationToken);
            var decision = CommandGatePipeline.Evaluate(envelope, _machine, receipt, setup, _trackers.AnnotationLedger);

            switch (decision.Kind)
            {
                case GateDecisionKind.Reject:
                    return Reject(envelope, decision.Rejection!);
                case GateDecisionKind.Duplicate:
                    return await SessionCommit.ReplayAsync(
                        _store,
                        GameId,
                        _logger,
                        envelope,
                        decision.Receipt!,
                        cancellationToken);
                default:
                    break;
            }

            if (envelope.Command is RebuildRoomCommand rebuild)
            {
                return await RebuildAsync(envelope, rebuild, cancellationToken);
            }

            var settlement = SessionSettlement.BuildContext(
                _setup,
                _state,
                _abilities,
                _standingEffects,
                _machine);
            var dispatch = GameCommandDispatcher.Dispatch(
                envelope,
                _machine,
                setup,
                _trackers.AnnotationLedger,
                settlement,
                GameId,
                _logger);
            if (dispatch.Rejection is not null)
            {
                return Reject(envelope, dispatch.Rejection);
            }

            var recordedAt = _clock.UtcNow;

            // 补全「变化前角色」（R-0029）：产出方只报新值，由提交管线用**提交前**的账补齐——
            // 否则「恶魔 → 非恶魔」在账被覆盖后无从读出，胜负求值只能看见"现在没有恶魔"，
            // 与"从未配置恶魔"混为一谈（R-0024 第 4 条）。
            var businessEvents = SessionCommit.FillPreviousCharacters(dispatch.Events, _state);
            var drafts = new List<StoredEventDraft>(businessEvents.Count);
            var sequence = SessionCommit.AppendDrafts(drafts, businessEvents, _lastSequence, recordedAt);

            // 先把这一步的账在内存里折出来：折不动就整条命令失败，绝不落库。
            // 否则会留下"事件已落库、账没折"的中间态，而重投会被当成 Duplicate —— 分叉永远暴露不出来。
            var nextState = FoldLedger(_state, drafts);
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
                LogDiagnostics(reconciliation.Diagnostics);

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
                LogDiagnostics(housekeeping.Diagnostics);
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
                    GameId,
                    _logger);
            }

            await _store.CommitAsync(
                new GameCommit
                {
                    GameId = GameId,
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
                        FirstSequence = _lastSequence + 1,
                        LastSequence = sequence,
                    },
                },
                cancellationToken);

            var previousMachine = _machine;
            _machine = nextMachine;
            _state = nextState;
            _lastSequence = sequence;
            var publicSurfaceChanged = _trackers.Update(drafts, recordedAt);
            var notifications = GameNotificationBuilder.Build(drafts, previousMachine, publicSurfaceChanged);
            _logger.LogInformation(
                "命令已接受：game={GameId} actor={ActorKind} command={Command} 事件数={EventCount} 派生事件数={DerivedCount} 序号={Sequence} 挂起={Held} clientSequence={ClientSequence}",
                GameId,
                envelope.Actor.Kind,
                envelope.Command.GetType().Name,
                businessEvents.Count,
                derivedEvents.Count,
                _lastSequence,
                _machine?.IsHeld,
                envelope.ClientSequence);

            return new CommandResult
            {
                Kind = CommandResultKind.Accepted,
                Sequence = _lastSequence,
                Events = businessEvents,
                Notifications = notifications,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(
                exception,
                "命令处理失败：game={GameId} actor={ActorKind} command={Command} key={Key}",
                GameId,
                envelope.Actor.Kind,
                envelope.Command.GetType().Name,
                envelope.IdempotencyKey);

            return new CommandResult
            {
                Kind = CommandResultKind.Failed,
                Sequence = _lastSequence,
                Events = [],
                Notifications = [],
                Failure = exception.Message,
            };
        }
    }

    private async Task<CommandResult> RebuildAsync(
        CommandEnvelope envelope,
        RebuildRoomCommand rebuild,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await RoomRebuildService.RebuildAsync(
                GameId,
                _store,
                _machine,
                _state,
                envelope.IdempotencyKey,
                _clock.UtcNow,
                _logger,
                cancellationToken);

            _machine = outcome.Machine;
            _state = outcome.State;
            _health = RoomHealth.Healthy;
            _lastSequence = outcome.Sequence;
            _trackers.Recover(outcome.Events, outcome.Machine);

            _logger.LogWarning(
                "房间已按事件日志重建：game={GameId} reason={Reason} 内存一致={MachineEquivalent} 快照一致={SnapshotEquivalent} 账一致={LedgerEquivalent} 事件数={EventCount} 序号={Sequence}",
                GameId,
                rebuild.Reason,
                outcome.MachineEquivalent,
                outcome.SnapshotEquivalent,
                outcome.LedgerEquivalent,
                outcome.Events.Count,
                outcome.Sequence);

            return new CommandResult
            {
                Kind = CommandResultKind.Accepted,
                Sequence = outcome.Sequence,
                Events = [],
                Notifications =
                [
                    new GameNotification { Kind = GameNotificationKind.RoomRebuilt, Sequence = outcome.Sequence },
                    new GameNotification { Kind = GameNotificationKind.StorytellerViewChanged, Sequence = outcome.Sequence },
                ],
                Rebuild = new RoomRebuildReport
                {
                    MachineEquivalent = outcome.MachineEquivalent,
                    SnapshotEquivalent = outcome.SnapshotEquivalent,
                    LedgerEquivalent = outcome.LedgerEquivalent,
                    Sequence = outcome.Sequence,
                },
            };
        }
        catch (InvalidOperationException exception)
        {
            // 重建失败 = 事件日志暂时不可用：降级位置 / 留，原因更新，等下一次显式重建；
            // 绝不返回"等价"假结论（CommandResult.Rebuild 保持 null）。
            _health = _health.Degrade($"重建失败：{exception.Message}", _clock.UtcNow);
            _logger.LogError(
                exception,
                "房间重建失败（显式报错，不静默继续）：game={GameId} reason={Reason} 健康位降级={Degraded} 原因={HealthReason}",
                GameId,
                rebuild.Reason,
                _health.IsDegraded,
                _health.Reason);

            return new CommandResult
            {
                Kind = CommandResultKind.Failed,
                Sequence = _lastSequence,
                Events = [],
                // 失败也改了视图：健康位的原因被更新。不推的话，说书人看到的还是上一次的原因，
                // "显式报告"就退化成"必须手点刷新"——通知面必须与派生状态同源。
                Notifications = [new GameNotification { Kind = GameNotificationKind.StorytellerViewChanged, Sequence = _lastSequence }],
                Failure = $"事件日志无法重建：{exception.Message}",
            };
        }
    }

    /// <summary>
    /// 取本局会话信息（席位名单）：读一次目录后缓存（记录不可变）。
    /// 结算的座次、常驻效果对账、开夜与分配都靠它；读不到时相关命令照旧显式拒绝。
    /// </summary>
    private async Task<GameSetup?> EnsureSetupAsync(CancellationToken cancellationToken) =>
        _setup ??= await _catalog.FindAsync(GameId, cancellationToken);

    /// <summary>本局席位名单（投影用）；会话信息还没读到时为空表——宁可少给可提名目标，不猜。</summary>
    private IReadOnlyList<SeatId> SeatList() =>
        _setup is { } setup ? [.. setup.Seats.Select(item => item.Seat)] : [];

    private CommandResult Reject(CommandEnvelope envelope, CommandRejection rejection)
    {
        _logger.LogWarning(
            "命令被拒绝：game={GameId} gate={Gate} code={Code} actor={ActorKind} seat={Seat} command={Command} 说明={Message}",
            GameId,
            rejection.Gate,
            rejection.Code,
            envelope.Actor.Kind,
            envelope.Actor.Seat,
            envelope.Command.GetType().Name,
            rejection.Message);

        return new CommandResult
        {
            Kind = CommandResultKind.Rejected,
            // 拒绝回执刻意不带全局序号：带上它，任何玩家都能用伪造命令的差分探测房间活动节奏（D-0013 §5）。
            Sequence = 0,
            Events = [],
            Notifications = [],
            Rejection = rejection,
        };
    }

    /// <summary>
    /// 把一批事件折进状态账的**副本**（与步骤机状态同源、同一批事件）。
    /// </summary>
    /// <remarks>
    /// 刻意不改 <see cref="_state"/>：调用方先在内存里折成功，才允许把事件提交落库，
    /// 提交成功后才把结果赋回去。这样"折不动"等价于"这条命令失败"，不会出现半提交的账。
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

    /// <summary>对账诊断落调试日志："本次没有重算"是输入不全时的诚实结论，不是错误。</summary>
    private void LogDiagnostics(IReadOnlyList<string> diagnostics) =>
        SessionCommit.LogDiagnostics(diagnostics, _logger, GameId);
}
