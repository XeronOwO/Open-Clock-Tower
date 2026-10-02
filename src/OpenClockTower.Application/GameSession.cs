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
            if (machine is null || machine.IsPlanCompleted)
            {
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
                _lastSequence,
                seat,
                _trackers.InformationResultsFor(seat));
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
            var clamped = Math.Clamp(afterSequence, 0, _lastSequence);
            var all = await _store.ReadEventsAsync(GameId, afterSequence: 0, cancellationToken);
            var addressees = PlayerEventProjection.AddresseeLookup(all);

            var events = new List<PlayerEvent>();
            foreach (var stored in all)
            {
                if (stored.Sequence <= clamped)
                {
                    continue;
                }

                var playerEvent = PlayerEventProjection.ForSeat(stored, seat, addressees);
                if (playerEvent is not null)
                {
                    events.Add(playerEvent);
                }
            }

            return new ReconnectBundle
            {
                Sequence = _lastSequence,
                View = GameProjection.ForSeat(
                    _machine,
                    _lastSequence,
                    seat,
                    _trackers.InformationResultsFor(seat)),
                EventsSince = events,
            };
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
            var decision = CommandGatePipeline.Evaluate(envelope, _machine, receipt, setup);

            switch (decision.Kind)
            {
                case GateDecisionKind.Reject:
                    return Reject(envelope, decision.Rejection!);
                case GateDecisionKind.Duplicate:
                    return await ReplayAsync(envelope, decision.Receipt!, cancellationToken);
                default:
                    break;
            }

            if (envelope.Command is RebuildRoomCommand rebuild)
            {
                return await RebuildAsync(envelope, rebuild, cancellationToken);
            }

            var settlement = SessionSettlement.BuildContext(_setup, _state, _abilities, _standingEffects);
            var dispatch = GameCommandDispatcher.Dispatch(
                envelope,
                _machine,
                setup,
                settlement,
                GameId,
                _logger);
            if (dispatch.Rejection is not null)
            {
                return Reject(envelope, dispatch.Rejection);
            }

            var recordedAt = _clock.UtcNow;
            var drafts = new List<StoredEventDraft>(dispatch.Events.Count);
            var sequence = _lastSequence;
            foreach (var gameEvent in dispatch.Events)
            {
                sequence++;
                drafts.Add(new StoredEventDraft
                {
                    Sequence = sequence,
                    Event = gameEvent,
                    RecordedAt = recordedAt,
                });
            }

            // 先把这一步的账在内存里折出来：折不动就整条命令失败，绝不落库。
            // 否则会留下"事件已落库、账没折"的中间态，而重投会被当成 Duplicate —— 分叉永远暴露不出来。
            var nextState = FoldLedger(_state, drafts);

            // 固定点对账：常驻效果（诺-达鲺的中毒等）与由效果压制的维度重算（D-0015 推论 1）。
            // 派生事件与业务事件**同一次提交**落库；重放只折事件，恢复不重算。
            var reconciliation = SessionSettlement.Reconcile(nextState, settlement);
            foreach (var diagnostic in reconciliation.Diagnostics)
            {
                _logger.LogDebug(
                    "结算对账本次未重算：{Diagnostic} game={GameId}",
                    diagnostic,
                    GameId);
            }

            foreach (var derived in reconciliation.Events)
            {
                sequence++;
                drafts.Add(new StoredEventDraft
                {
                    Sequence = sequence,
                    Event = derived,
                    RecordedAt = recordedAt,
                });
            }

            nextState = reconciliation.State;

            await _store.CommitAsync(
                new GameCommit
                {
                    GameId = GameId,
                    Events = drafts,
                    Snapshot = new StoredSnapshot
                    {
                        Sequence = sequence,
                        Machine = dispatch.Machine,
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
            _machine = dispatch.Machine;
            _state = nextState;
            _lastSequence = sequence;
            _trackers.Update(drafts, recordedAt);

            var notifications = GameNotificationBuilder.Build(dispatch.Events, previousMachine);
            _logger.LogInformation(
                "命令已接受：game={GameId} actor={ActorKind} command={Command} 事件数={EventCount} 派生事件数={DerivedCount} 序号={Sequence} 挂起={Held} clientSequence={ClientSequence}",
                GameId,
                envelope.Actor.Kind,
                envelope.Command.GetType().Name,
                dispatch.Events.Count,
                reconciliation.Events.Count,
                _lastSequence,
                _machine?.IsHeld,
                envelope.ClientSequence);

            return new CommandResult
            {
                Kind = CommandResultKind.Accepted,
                Sequence = _lastSequence,
                Events = dispatch.Events,
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

    private async Task<CommandResult> ReplayAsync(
        CommandEnvelope envelope,
        CommandReceipt receipt,
        CancellationToken cancellationToken)
    {
        var events = new List<GameEvent>();
        if (!receipt.IsEmpty)
        {
            var stored = await _store.ReadEventsAsync(GameId, receipt.FirstSequence - 1, cancellationToken);
            events.AddRange(
                stored.Where(item => item.Sequence <= receipt.LastSequence).Select(item => item.Event));
        }

        _logger.LogInformation(
            "重复投递按回执重放：game={GameId} key={Key} command={Command} 序号={First}..{Last} clientSequence={ClientSequence}",
            GameId,
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

    private async Task<CommandResult> RebuildAsync(
        CommandEnvelope envelope,
        RebuildRoomCommand rebuild,
        CancellationToken cancellationToken)
    {
        try
        {
            var storedEvents = await _store.ReadEventsAsync(GameId, afterSequence: 0, cancellationToken);
            var eventStream = storedEvents.Select(item => item.Event).ToArray();
            var rebuilt = eventStream.Length == 0 ? null : StepMachine.Fold(eventStream);

            // 状态账与步骤机同源折叠：同一条事件流，两个派生视图必须一起重算，否则重建后账会陈旧。
            var rebuiltState = GameStateMachine.Fold(eventStream);
            var machineEquivalent = StepMachineStateComparer.AreEquivalent(_machine, rebuilt);
            bool? snapshotEquivalent;
            try
            {
                var snapshot = await _store.FindSnapshotAsync(GameId, cancellationToken);
                snapshotEquivalent = snapshot is null
                    ? null
                    : StepMachineStateComparer.AreEquivalent(snapshot.Machine, rebuilt);
            }
            catch (InvalidOperationException exception)
            {
                // 旧快照读不出来 = 与事件流不一致；快照是派生数据，重建就是来修它的
                _logger.LogWarning(exception, "读取旧快照失败，按不一致处理：game={GameId}", GameId);
                snapshotEquivalent = false;
            }
            var lastSequence = storedEvents.Count == 0 ? 0 : storedEvents[^1].Sequence;
            var recordedAt = _clock.UtcNow;

            await _store.CommitAsync(
                new GameCommit
                {
                    GameId = GameId,
                    Events = [],
                    Snapshot = new StoredSnapshot
                    {
                        Sequence = lastSequence,
                        Machine = rebuilt,
                        RecordedAt = recordedAt,
                    },
                    Receipt = new CommandReceipt
                    {
                        IdempotencyKey = envelope.IdempotencyKey,
                        FirstSequence = lastSequence + 1,
                        LastSequence = lastSequence,
                    },
                },
                cancellationToken);

            _machine = rebuilt;
            _state = rebuiltState;
            _lastSequence = lastSequence;
            _trackers.Recover(storedEvents, rebuilt);

            _logger.LogWarning(
                "房间已按事件日志重建：game={GameId} reason={Reason} 内存一致={MachineEquivalent} 快照一致={SnapshotEquivalent} 事件数={EventCount} 序号={Sequence}",
                GameId,
                rebuild.Reason,
                machineEquivalent,
                snapshotEquivalent,
                storedEvents.Count,
                lastSequence);

            return new CommandResult
            {
                Kind = CommandResultKind.Accepted,
                Sequence = lastSequence,
                Events = [],
                Notifications =
                [
                    new GameNotification { Kind = GameNotificationKind.RoomRebuilt },
                    new GameNotification { Kind = GameNotificationKind.StorytellerViewChanged },
                ],
                Rebuild = new RoomRebuildReport
                {
                    MachineEquivalent = machineEquivalent,
                    SnapshotEquivalent = snapshotEquivalent,
                    Sequence = lastSequence,
                },
            };
        }
        catch (InvalidOperationException exception)
        {
            _logger.LogError(
                exception,
                "房间重建失败（显式报错，不静默继续）：game={GameId} reason={Reason}",
                GameId,
                rebuild.Reason);

            return new CommandResult
            {
                Kind = CommandResultKind.Failed,
                Sequence = _lastSequence,
                Events = [],
                Notifications = [],
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
            Sequence = _lastSequence,
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

}
