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
    private readonly IClock _clock;
    private readonly PacingOptions _pacing;
    private readonly ILogger<GameSession> _logger;
    private readonly SemaphoreSlim _gate = new(initialCount: 1, maxCount: 1);

    private const int RecentSeatChangeCapacity = 20;

    private readonly List<SeatChangeSnapshot> _recentSeatChanges = [];

    private StepMachineState? _machine;
    private long _lastSequence;
    private DateTimeOffset? _slotStartedAt;
    private DateTimeOffset? _pendingRequestSince;

    /// <summary>构造一局的编排器。</summary>
    public GameSession(
        GameId gameId,
        IGameStore store,
        IClock clock,
        PacingOptions pacing,
        ILogger<GameSession> logger)
    {
        GameId = gameId;
        _store = store;
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
                var machine = default(StepMachineState);
                var lastSequence = 0L;
                var lastSlotEnteredAt = default(DateTimeOffset?);

                foreach (var stored in storedEvents)
                {
                    machine = StepMachine.Apply(machine, stored.Event);
                    lastSequence = stored.Sequence;
                    if (stored.Event is SlotEnteredEvent)
                    {
                        lastSlotEnteredAt = stored.RecordedAt;
                    }
                }

                _machine = machine;
                _lastSequence = lastSequence;
                _slotStartedAt = lastSlotEnteredAt;
                RecoverPendingSince(storedEvents, machine);
                RecoverRecentSeatChanges(storedEvents);

                _logger.LogInformation(
                    "步骤机状态已从事件流恢复：game={GameId} 事件数={EventCount} 序号={Sequence} 槽位={SlotIndex} 挂起={Held}",
                    GameId,
                    storedEvents.Count,
                    _lastSequence,
                    _machine?.SlotIndex,
                    _machine?.IsHeld);
            }
            catch (InvalidOperationException)
            {
                // 事件流损坏：停在空状态，但保留序号连续性——新事件不得与库里旧行的主键冲突
                _machine = null;
                _lastSequence = await _store.FindLastSequenceAsync(GameId, cancellationToken);
                _slotStartedAt = null;
                _pendingRequestSince = null;
                _recentSeatChanges.Clear();
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
    /// 等说书人重建或强推。
    /// </remarks>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var machine = _machine;
            if (machine is null || machine.IsPlanCompleted)
            {
                return;
            }

            if (machine.Control != ControlMode.Automatic || machine.Quota != SlotQuotaState.Running)
            {
                return;
            }

            if (_slotStartedAt is not { } startedAt || _clock.UtcNow < startedAt + _pacing.SlotQuota)
            {
                return;
            }

            var slot = machine.CurrentSlot!;
            var envelope = new CommandEnvelope
            {
                Command = new SlotQuotaElapsedCommand(),
                Actor = Actor.System,
                IdempotencyKey = $"slot-elapsed:{machine.Plan.Label}:{slot.Id}",
            };
            await ExecuteCoreAsync(envelope, cancellationToken);
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
            return GameProjection.ForSeat(_machine, _lastSequence, seat);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>取说书人视图（含卡点、状态变化归因与兜底所需的一切）。</summary>
    public StorytellerView GetStorytellerView()
    {
        _gate.Wait();
        try
        {
            return GameProjection.ForStoryteller(
                _machine,
                _lastSequence,
                _pendingRequestSince,
                _clock.UtcNow,
                [.. _recentSeatChanges]);
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
        var clamped = Math.Clamp(afterSequence, 0, _lastSequence);
        var all = await _store.ReadEventsAsync(GameId, afterSequence: 0, cancellationToken);
        var addressees = BuildAddresseeLookup(all);

        var events = new List<PlayerEvent>();
        foreach (var stored in all)
        {
            if (stored.Sequence <= clamped)
            {
                continue;
            }

            var playerEvent = ProjectForSeat(stored, seat, addressees);
            if (playerEvent is not null)
            {
                events.Add(playerEvent);
            }
        }

        return new ReconnectBundle
        {
            Sequence = _lastSequence,
            View = GameProjection.ForSeat(_machine, _lastSequence, seat),
            EventsSince = events,
        };
    }

    private async Task<CommandResult> ExecuteCoreAsync(CommandEnvelope envelope, CancellationToken cancellationToken)
    {
        try
        {
            var receipt = await _store.FindReceiptAsync(GameId, envelope.IdempotencyKey, cancellationToken);
            var decision = CommandGatePipeline.Evaluate(envelope, _machine, receipt);

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

            var dispatch = Dispatch(envelope);
            if (dispatch.Rejection is not null)
            {
                return Reject(envelope, dispatch.Rejection);
            }

            var kernelOutcome = dispatch.Outcome!;
            var recordedAt = _clock.UtcNow;
            var drafts = new List<StoredEventDraft>(kernelOutcome.Events.Count);
            var sequence = _lastSequence;
            foreach (var gameEvent in kernelOutcome.Events)
            {
                sequence++;
                drafts.Add(new StoredEventDraft
                {
                    Sequence = sequence,
                    Event = gameEvent,
                    RecordedAt = recordedAt,
                });
            }

            await _store.CommitAsync(
                new GameCommit
                {
                    GameId = GameId,
                    Events = drafts,
                    Snapshot = new StoredSnapshot
                    {
                        Sequence = sequence,
                        Machine = kernelOutcome.State,
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
            _machine = kernelOutcome.State;
            _lastSequence = sequence;
            Track(drafts, recordedAt);

            var notifications = GameNotificationBuilder.Build(kernelOutcome.Events, previousMachine);
            _logger.LogInformation(
                "命令已接受：game={GameId} actor={ActorKind} command={Command} 事件数={EventCount} 序号={Sequence} 挂起={Held} clientSequence={ClientSequence}",
                GameId,
                envelope.Actor.Kind,
                envelope.Command.GetType().Name,
                kernelOutcome.Events.Count,
                _lastSequence,
                _machine.IsHeld,
                envelope.ClientSequence);

            return new CommandResult
            {
                Kind = CommandResultKind.Accepted,
                Sequence = _lastSequence,
                Events = kernelOutcome.Events,
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
            var rebuilt = storedEvents.Count == 0
                ? null
                : StepMachine.Fold(storedEvents.Select(item => item.Event));
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
            _lastSequence = lastSequence;
            RecoverTrackers(storedEvents, rebuilt);
            RecoverRecentSeatChanges(storedEvents);

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

    private (StepMachineOutcome? Outcome, CommandRejection? Rejection) Dispatch(CommandEnvelope envelope)
    {
        if (envelope.Command is StartPhaseCommand start)
        {
            return (StepMachine.StartPhase(start.Plan, start.Control), null);
        }

        if (_machine is null)
        {
            return (
                null,
                new CommandRejection
                {
                    Code = "kernel.not_started",
                    Message = "步骤机尚未开启任何阶段",
                    Gate = "kernel",
                });
        }

        var input = ToKernelInput(envelope.Command);
        if (input is null)
        {
            return (
                null,
                new CommandRejection
                {
                    Code = "kernel.unsupported",
                    Message = $"未支持的命令：{envelope.Command.GetType().Name}",
                    Gate = "kernel",
                });
        }

        var outcome = StepMachine.Handle(_machine, input);
        if (outcome.Kind == StepMachineOutcomeKind.Rejected)
        {
            return (
                null,
                new CommandRejection
                {
                    Code = $"kernel.{outcome.RejectionReason}",
                    Message = outcome.RejectionNote ?? "内核拒绝了这条输入",
                    Gate = "kernel",
                });
        }

        return (outcome, null);
    }

    private static StepMachineInput? ToKernelInput(GameCommand command) => command switch
    {
        SubmitResponseCommand submit => new SubmitResponseInput
        {
            RequestId = submit.RequestId,
            OptionValue = submit.OptionValue,
            Source = ResponseSource.Player,
        },
        ProxyFillCommand proxy => new SubmitResponseInput
        {
            RequestId = proxy.RequestId,
            OptionValue = proxy.OptionValue,
            Source = ResponseSource.StorytellerProxy,
            Note = proxy.Note,
        },
        VoidRequestCommand voidRequest => new VoidRequestInput
        {
            RequestId = voidRequest.RequestId,
            Reason = voidRequest.Reason,
            Note = voidRequest.Note,
        },
        ForceAdvanceCommand force => new ForceAdvanceInput { Reason = force.Reason },
        TakeOverCommand takeOver => new TakeOverInput { Reason = takeOver.Reason },
        ReleaseControlCommand release => new ReleaseControlInput { Reason = release.Reason },
        ResolveDecisionPointCommand resolve => new ResolveDecisionPointInput
        {
            DecisionPointId = resolve.DecisionPointId,
            Decision = resolve.Decision,
            Note = resolve.Note,
        },
        ApplySeatStateCommand seat => new SeatStateChangedInput
        {
            Seat = seat.Seat,
            Life = seat.Life,
            Character = seat.Character,
            Reason = seat.Reason,
            CausedBy = seat.CausedBy,
        },
        SlotQuotaElapsedCommand => new SlotQuotaElapsedInput(),
        _ => null,
    };

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

    private void Track(IReadOnlyList<StoredEventDraft> drafts, DateTimeOffset recordedAt)
    {
        foreach (var draft in drafts)
        {
            switch (draft.Event)
            {
                case SlotEnteredEvent:
                    _slotStartedAt = recordedAt;
                    _pendingRequestSince = null;
                    break;
                case OperationRequestIssuedEvent:
                    _pendingRequestSince = recordedAt;
                    break;
                case OperationRequestAnsweredEvent:
                case OperationRequestVoidedEvent:
                case SlotAdvancedEvent:
                case SlotForceAdvancedEvent:
                    _pendingRequestSince = null;
                    break;
                case SeatStateChangedEvent seatChanged:
                    AppendRecentSeatChange(seatChanged, draft.Sequence, recordedAt);
                    break;
            }
        }
    }

    private void AppendRecentSeatChange(SeatStateChangedEvent seatChanged, long sequence, DateTimeOffset recordedAt)
    {
        _recentSeatChanges.Add(new SeatChangeSnapshot
        {
            Seat = seatChanged.Seat,
            Life = seatChanged.Life,
            Character = seatChanged.Character,
            Reason = seatChanged.Reason,
            CausedBy = seatChanged.CausedBy,
            Sequence = sequence,
            RecordedAt = recordedAt,
        });

        if (_recentSeatChanges.Count > RecentSeatChangeCapacity)
        {
            _recentSeatChanges.RemoveAt(0);
        }
    }

    private void RecoverRecentSeatChanges(IReadOnlyList<StoredEvent> storedEvents)
    {
        _recentSeatChanges.Clear();
        foreach (var stored in storedEvents)
        {
            if (stored.Event is SeatStateChangedEvent seatChanged)
            {
                AppendRecentSeatChange(seatChanged, stored.Sequence, stored.RecordedAt);
            }
        }
    }

    private void RecoverTrackers(IReadOnlyList<StoredEvent> storedEvents, StepMachineState? machine)
    {
        DateTimeOffset? lastSlotEnteredAt = null;
        foreach (var stored in storedEvents)
        {
            if (stored.Event is SlotEnteredEvent)
            {
                lastSlotEnteredAt = stored.RecordedAt;
            }
        }

        _slotStartedAt = lastSlotEnteredAt;
        RecoverPendingSince(storedEvents, machine);
    }

    private void RecoverPendingSince(IReadOnlyList<StoredEvent> storedEvents, StepMachineState? machine)
    {
        OperationRequestId? lastIssuedRequestId = null;
        DateTimeOffset? lastIssuedAt = null;

        foreach (var stored in storedEvents)
        {
            if (stored.Event is OperationRequestIssuedEvent issued)
            {
                lastIssuedRequestId = issued.Request.Id;
                lastIssuedAt = stored.RecordedAt;
            }
        }

        var pending = machine?.PendingRequest;
        _pendingRequestSince = null;
        if (pending is { Status: OperationRequestStatus.Pending } && pending.Id == lastIssuedRequestId)
        {
            _pendingRequestSince = lastIssuedAt;
        }
    }

    private static Dictionary<string, SeatId> BuildAddresseeLookup(IReadOnlyList<StoredEvent> storedEvents)
    {
        var lookup = new Dictionary<string, SeatId>(StringComparer.Ordinal);
        foreach (var stored in storedEvents)
        {
            if (stored.Event is OperationRequestIssuedEvent issued)
            {
                lookup[issued.Request.Id.Value] = issued.Request.Addressee;
            }
        }

        return lookup;
    }

    private static PlayerEvent? ProjectForSeat(
        StoredEvent stored,
        SeatId seat,
        IReadOnlyDictionary<string, SeatId> addressees) =>
        stored.Event switch
        {
            PhaseStartedEvent started => new PlayerEvent
            {
                Sequence = stored.Sequence,
                Kind = PlayerEventKind.PhaseStarted,
                Phase = started.Plan.Phase,
            },
            OperationRequestIssuedEvent issued when issued.Request.Addressee == seat => new PlayerEvent
            {
                Sequence = stored.Sequence,
                Kind = PlayerEventKind.RequestIssued,
                Request = issued.Request,
            },
            OperationRequestAnsweredEvent answered
                when IsRequestOfSeat(answered.RequestId, seat, addressees) => new PlayerEvent
                {
                    Sequence = stored.Sequence,
                    Kind = PlayerEventKind.RequestAnswered,
                    RequestId = answered.RequestId,
                    OptionValue = answered.Answer.OptionValue,
                },
            OperationRequestVoidedEvent voided
                when IsRequestOfSeat(voided.RequestId, seat, addressees) => new PlayerEvent
                {
                    Sequence = stored.Sequence,
                    Kind = PlayerEventKind.RequestVoided,
                    RequestId = voided.RequestId,
                    Void = voided.Void,
                },
            _ => null,
        };

    private static bool IsRequestOfSeat(
        OperationRequestId requestId,
        SeatId seat,
        IReadOnlyDictionary<string, SeatId> addressees) =>
        addressees.TryGetValue(requestId.Value, out var addressee) && addressee == seat;
}
