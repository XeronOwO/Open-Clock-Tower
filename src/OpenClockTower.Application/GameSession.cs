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
    private readonly SeatNameDirectory _seatNames;
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
    /// 席位名读模型（<see cref="SeatNameDirectory"/>）同理只读：它由 Server 在认领 / 改名时更新。
    /// </remarks>
    public GameSession(
        GameId gameId,
        IGameStore store,
        IGameCatalog catalog,
        IAbilityResolutionCatalog abilities,
        IReadOnlyList<IStandingEffectSource> standingEffects,
        IClock clock,
        PacingOptions pacing,
        SeatNameDirectory seatNames,
        ILogger<GameSession> logger)
    {
        GameId = gameId;
        _store = store;
        _catalog = catalog;
        _abilities = abilities;
        _standingEffects = standingEffects;
        _clock = clock;
        _pacing = pacing;
        _seatNames = seatNames;
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
    /// 节拍器心跳：把「配额到点」翻译成一条系统命令（时间 → 输入，D-0008）；
    /// 这一拍到底有没有输入由 <see cref="SlotQuotaPacer"/> 判定。
    /// </summary>
    /// <remarks>
    /// **返回本次心跳的提交结果**（没有到点时为 null）——调用方拿到结果后必须照常分发通知，
    /// 否则这一步产生的操作请求只会留在服务端。
    /// </remarks>
    public async Task<CommandResult?> TickAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var envelope = SlotQuotaPacer.TryBuild(_machine, _trackers, _clock.UtcNow, _pacing.SlotQuota)
                ?? VoteSweepPacer.TryBuild(_machine, _trackers, _clock.UtcNow);
            if (envelope is null)
            {
                return null;
            }

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
                _clock.UtcNow,
                _trackers.VoteSweepStartedAt,
                seat,
                _trackers,
                _seatNames.Snapshot());
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
                _abilities,
                _seatNames.Snapshot());
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 配板建议（只读）：按官方分布表 + 在场角色的设置调整生成建议；输入是**非旅行者人数**（R-0046），
    /// 席位按升序绑定到低号席（非旅行者部分），旅行者不参与配板。
    /// </summary>
    /// <remarks>
    /// 口径与求解都在 <see cref="SetupProposalQuery"/>（R-0041 / R-0042 / R-0046）：随机只作**显式输入**（种子），
    /// 建议不落账——说书人重摇 / 手改后仍走既有分配命令面提交（D-0017）。
    /// </remarks>
    /// <param name="nonTravellerCount">本局非旅行者人数；null = 全部席位都是非旅行者（缺省语义）。</param>
    public async Task<SetupProposalResult> ProposeSetupAsync(
        string? seed,
        int? nonTravellerCount,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // 席位名单是会话信息；步骤机尚未开始 = 仍在开局设置（与分配闸同一把尺子）。
            var setup = await EnsureSetupAsync(cancellationToken);
            return SetupProposalQuery.Build(GameId, setup, _machine is not null, seed, nonTravellerCount, _logger);
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
                _seatNames.Snapshot(),
                _clock.UtcNow,
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
            var commit = await SessionPipeline.CommitAsync(
                _store,
                GameId,
                _logger,
                envelope,
                dispatch,
                setup,
                _state,
                _machine,
                _trackers,
                settlement,
                _lastSequence,
                recordedAt,
                cancellationToken);

            _machine = commit.Machine;
            _state = commit.State;
            _lastSequence = commit.Sequence;
            return new CommandResult
            {
                Kind = CommandResultKind.Accepted,
                Sequence = commit.Sequence,
                Events = commit.Events,
                Notifications = commit.Notifications,
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
}
