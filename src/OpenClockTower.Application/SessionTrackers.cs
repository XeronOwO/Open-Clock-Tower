using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 会话级派生跟踪器：最近状态变化、最近一次能力结算、发给各席位的信息结果、公开生死面、
/// 说书人注记账（D-0019）、卡点起算与槽位起算。
/// </summary>
/// <remarks>
/// <para>
/// 这些都是**由事件流派生**的，不是新的事实来源：重启恢复走 <see cref="Recover"/>，
/// 提交后推进走 <see cref="Update"/>，事件流损坏停在空状态时走 <see cref="Clear"/>。
/// </para>
/// <para>
/// 真实时间只在应用层出现（D-0008），且一律由调用方在事件落库时给出——这里不读时钟。
/// </para>
/// </remarks>
public sealed class SessionTrackers
{
    private const int RecentSeatChangeCapacity = 20;

    private readonly List<SeatChangeSnapshot> _recentSeatChanges = [];
    private readonly List<(SeatId Recipient, InformationResultSnapshot Result)> _informationResults = [];
    private readonly Dictionary<StepSlotId, AbilityResolutionSnapshot> _slotResolutions = [];
    private PublicLifeBoard _publicLife = PublicLifeBoard.Empty;
    private SeatAnnotationLedger _annotations = SeatAnnotationLedger.Empty;

    /// <summary>当前槽位的起算时刻；没有起点信息（异常数据）时为 null，此时宁可不动。</summary>
    public DateTimeOffset? SlotStartedAt { get; private set; }

    /// <summary>
    /// 本次槽位进入的事件序号（重进本格 = 新序号）；没有起点信息时为 null。
    /// 配额输入的幂等键按它区分**每一次进入**——只按「计划 + 槽位」做键时，重进本格后的第二次配额
    /// 会撞上上一次的收据、被当成重复命令回放，计划永久停在原地
    /// （回归见 <c>BarberHostTests.BarberSwapAnsweredAfterQuotaElapsed_PlanStillAdvances</c>）。
    /// </summary>
    public long? SlotEntrySequence { get; private set; }

    /// <summary>
    /// 钟盘收票的时间轴锚点（开始 / 继续事件的记录时刻）；为空 = 没有可推进的收票
    /// （未开始，或服务端重启 / 重建后中断、等待说书人继续）。
    /// </summary>
    public DateTimeOffset? VoteSweepStartedAt { get; private set; }

    /// <summary>收票锚点事件的序号（收票命令的幂等键按"这一次收票"区分）。</summary>
    public long? VoteSweepEntrySequence { get; private set; }

    /// <summary>锚点属于哪条选票（提名 / 流放）；没有锚点时为 null。</summary>
    public BallotKind? VoteSweepBallotKind { get; private set; }

    /// <summary>锚点属于当天第几项提名 / 第几条流放；没有锚点时为 null。与族一起防两条收票串台。</summary>
    public int? VoteSweepBallotIndex { get; private set; }

    /// <summary>当前挂起请求的起算时刻；没有挂起时为 null（说书人视图据此算"卡了多久"）。</summary>
    public DateTimeOffset? PendingRequestSince { get; private set; }

    /// <summary>公开生死面（生命标记等价物 + 本日生死公告）；由事件流按 `rulings.md` R-0022 折叠。</summary>
    public PublicLifeBoard PublicLife => _publicLife;

    /// <summary>
    /// 说书人注记账（D-0019）：与状态账同源折叠、但独立成账——自由文本不进六维度（D-0015）。
    /// 说书人视图按它投影；玩家投影里没有它（D-0012 §4.3）。
    /// </summary>
    public SeatAnnotationLedger AnnotationLedger => _annotations;

    /// <summary>最近的状态变化（最新在后）；说书人视图的「刚发生了什么」。</summary>
    public IReadOnlyList<SeatChangeSnapshot> RecentSeatChanges => [.. _recentSeatChanges];

    /// <summary>最近一次能力结算的结论；还没有结算过时为 null。</summary>
    public AbilityResolutionSnapshot? LastResolution { get; private set; }

    /// <summary>最近一次被作废的操作请求；还没有作废过时为 null。</summary>
    public VoidedRequestSnapshot? LastVoidedRequest { get; private set; }

    /// <summary>
    /// 最近一次离场裁定（D-0037）；还没有裁定过时为 null。
    /// 用途是让提出申请的旅行者在**刷新之后仍然看得到结果**（"被驳回"是事件，不是状态）。
    /// </summary>
    public DepartureRulingSnapshot? LastDepartureRuling { get; private set; }

    /// <summary>取**本计划内**某个槽位已结算的能力结论；还没结算为 null。</summary>
    public AbilityResolutionSnapshot? ResolutionFor(StepSlotId slotId) =>
        _slotResolutions.GetValueOrDefault(slotId);

    /// <summary>取某个席位收到的全部信息结果（按发生顺序）；投影时只把它给收件人。</summary>
    public IReadOnlyList<InformationResultSnapshot> InformationResultsFor(SeatId seat) =>
        [.. _informationResults.Where(item => item.Recipient == seat).Select(item => item.Result)];

    /// <summary>把一批**已提交**的事件记进跟踪器。</summary>
    /// <param name="drafts">刚提交的事件草案（带序号与发生时刻）。</param>
    /// <param name="recordedAt">宿主记录的发生时刻。</param>
    /// <returns>本批是否让**公开生死面**（`Lives` / `Announcements`）实际变化——夜晚挂起不算。</returns>
    public bool Update(IReadOnlyList<StoredEventDraft> drafts, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        var revisionBefore = _publicLife.PublicRevision;
        foreach (var draft in drafts)
        {
            _publicLife = PublicLifeBoardFolder.Apply(_publicLife, draft.Event);
            switch (draft.Event)
            {
                case PhaseStartedEvent:
                    // 新计划开启：槽位标识跨夜复用（如 clockmaker），逐槽位结算只在本计划内有意义。
                    _slotResolutions.Clear();
                    ClearBallotAnchor();
                    break;
                case SlotEnteredEvent:
                    SlotStartedAt = recordedAt;
                    SlotEntrySequence = draft.Sequence;
                    PendingRequestSince = null;
                    break;
                case VoteSweepStartedEvent nominationSweep:
                    // 开始 / 继续收票：重锚时间轴，逐席到点由 VoteSweepPacer 按它算（D-0008）。
                    AnchorBallot(BallotKind.Nomination, nominationSweep.NominationIndex, recordedAt, draft.Sequence);
                    break;
                case VoteSweepResumedEvent nominationResumed:
                    AnchorBallot(BallotKind.Nomination, nominationResumed.NominationIndex, recordedAt, draft.Sequence);
                    break;
                case ExileSweepStartedEvent exileSweep:
                    AnchorBallot(BallotKind.Exile, exileSweep.ExileIndex, recordedAt, draft.Sequence);
                    break;
                case ExileSweepResumedEvent exileResumed:
                    AnchorBallot(BallotKind.Exile, exileResumed.ExileIndex, recordedAt, draft.Sequence);
                    break;
                // 收票收口：只退场**属于这条选票**的锚点——另一条收票可能正占着钟盘
                // （D2 实施口径：收票已收完但未计票的选票不占钟盘，计票可以延后）。
                case VoteCountedEvent counted when VoteSweepBallotKind == BallotKind.Nomination
                    && VoteSweepBallotIndex == counted.NominationIndex:
                case ExileVoteCountedEvent exileCounted when VoteSweepBallotKind == BallotKind.Exile
                    && VoteSweepBallotIndex == exileCounted.ExileIndex:
                case DayClosedEvent:
                    ClearBallotAnchor();
                    break;
                case OperationRequestIssuedEvent:
                    PendingRequestSince = recordedAt;
                    break;
                case OperationRequestAnsweredEvent:
                case SlotAdvancedEvent:
                case SlotForceAdvancedEvent:
                    PendingRequestSince = null;
                    break;
                case OperationRequestVoidedEvent voided:
                    LastVoidedRequest = ToVoidSnapshot(voided, draft.Sequence);
                    PendingRequestSince = null;
                    break;
                case SeatStateChangedEvent seatChanged:
                    AppendSeatChange(seatChanged, draft.Sequence, recordedAt);
                    break;
                case AbilityResolvedEvent resolved:
                    LastResolution = ToSnapshot(resolved, draft.Sequence);
                    _slotResolutions[resolved.SlotId] = LastResolution;
                    break;
                case InformationResultIssuedEvent information:
                    AppendInformationResult(information, draft.Sequence);
                    break;

                // 离场申请与裁定（D-0037）：新申请提出即清掉上一次结论（同一席位只留最新一次），
                // 裁定落下即记下结论——它要让申请人刷新后仍看得到"批了还是驳了"。
                case TravellerDepartureRequestedEvent:
                    LastDepartureRuling = null;
                    break;
                case TravellerDepartureResolvedEvent departureResolved:
                    LastDepartureRuling = new DepartureRulingSnapshot
                    {
                        Seat = departureResolved.Seat,
                        Approved = departureResolved.Approved,
                        Note = departureResolved.Note,
                        Sequence = draft.Sequence,
                    };
                    break;

                // 说书人注记（D-0019）：独立注记账，与状态账同源折叠。
                case SeatAnnotationAddedEvent:
                case SeatAnnotationUpdatedEvent:
                case SeatAnnotationRemovedEvent:
                    _annotations = SeatAnnotationMachine.Apply(_annotations, draft.Event);
                    break;
            }
        }

        return _publicLife.PublicRevision != revisionBefore;
    }

    /// <summary>从事件流重建（服务端重启恢复 / 房间重建）。</summary>
    /// <param name="storedEvents">全量事件。</param>
    /// <param name="machine">重放得到的步骤机状态；用它判断"上一条请求是否仍挂着"。</param>
    public void Recover(IReadOnlyList<StoredEvent> storedEvents, StepMachineState? machine)
    {
        ArgumentNullException.ThrowIfNull(storedEvents);

        _recentSeatChanges.Clear();
        _informationResults.Clear();
        _slotResolutions.Clear();
        _publicLife = PublicLifeBoard.Empty;
        _annotations = SeatAnnotationLedger.Empty;
        LastResolution = null;
        LastVoidedRequest = null;
        LastDepartureRuling = null;
        SlotStartedAt = null;
        PendingRequestSince = null;
        DateTimeOffset? lastSlotEnteredAt = null;
        long? lastSlotEnteredSequence = null;
        long? lastSweepAnchorSequence = null;
        BallotKind? lastSweepKind = null;
        int? lastSweepIndex = null;
        OperationRequestId? lastIssuedRequestId = null;
        DateTimeOffset? lastIssuedAt = null;

        foreach (var stored in storedEvents)
        {
            _publicLife = PublicLifeBoardFolder.Apply(_publicLife, stored.Event);
            switch (stored.Event)
            {
                case PhaseStartedEvent:
                    _slotResolutions.Clear();
                    lastSweepKind = null;
                    lastSweepIndex = null;
                    lastSweepAnchorSequence = null;
                    break;
                case SlotEnteredEvent:
                    lastSlotEnteredAt = stored.RecordedAt;
                    lastSlotEnteredSequence = stored.Sequence;
                    break;
                case VoteSweepStartedEvent nominationSweep:
                    lastSweepKind = BallotKind.Nomination;
                    lastSweepIndex = nominationSweep.NominationIndex;
                    lastSweepAnchorSequence = stored.Sequence;
                    break;
                case VoteSweepResumedEvent nominationResumed:
                    lastSweepKind = BallotKind.Nomination;
                    lastSweepIndex = nominationResumed.NominationIndex;
                    lastSweepAnchorSequence = stored.Sequence;
                    break;
                case ExileSweepStartedEvent exileSweep:
                    lastSweepKind = BallotKind.Exile;
                    lastSweepIndex = exileSweep.ExileIndex;
                    lastSweepAnchorSequence = stored.Sequence;
                    break;
                case ExileSweepResumedEvent exileResumed:
                    lastSweepKind = BallotKind.Exile;
                    lastSweepIndex = exileResumed.ExileIndex;
                    lastSweepAnchorSequence = stored.Sequence;
                    break;
                // 收口只退场属于这条选票的锚点：另一条收票可能还占着钟盘（D2 实施口径）。
                case VoteCountedEvent counted when lastSweepKind == BallotKind.Nomination
                    && lastSweepIndex == counted.NominationIndex:
                case ExileVoteCountedEvent exileCounted when lastSweepKind == BallotKind.Exile
                    && lastSweepIndex == exileCounted.ExileIndex:
                case DayClosedEvent:
                    lastSweepKind = null;
                    lastSweepIndex = null;
                    lastSweepAnchorSequence = null;
                    break;
                case OperationRequestIssuedEvent issued:
                    lastIssuedRequestId = issued.Request.Id;
                    lastIssuedAt = stored.RecordedAt;
                    break;
                case OperationRequestVoidedEvent voided:
                    LastVoidedRequest = ToVoidSnapshot(voided, stored.Sequence);
                    break;
                case SeatStateChangedEvent seatChanged:
                    AppendSeatChange(seatChanged, stored.Sequence, stored.RecordedAt);
                    break;
                case AbilityResolvedEvent resolved:
                    LastResolution = ToSnapshot(resolved, stored.Sequence);
                    _slotResolutions[resolved.SlotId] = LastResolution;
                    break;
                case InformationResultIssuedEvent information:
                    AppendInformationResult(information, stored.Sequence);
                    break;

                // 离场申请与裁定（D-0037）：重启 / 重建按事件流恢复同一个"最近一次结论"。
                case TravellerDepartureRequestedEvent:
                    LastDepartureRuling = null;
                    break;
                case TravellerDepartureResolvedEvent departureResolved:
                    LastDepartureRuling = new DepartureRulingSnapshot
                    {
                        Seat = departureResolved.Seat,
                        Approved = departureResolved.Approved,
                        Note = departureResolved.Note,
                        Sequence = stored.Sequence,
                    };
                    break;

                // 说书人注记（D-0019）：重启 / 重建按事件流恢复同一本账。
                case SeatAnnotationAddedEvent:
                case SeatAnnotationUpdatedEvent:
                case SeatAnnotationRemovedEvent:
                    _annotations = SeatAnnotationMachine.Apply(_annotations, stored.Event);
                    break;
            }
        }

        SlotStartedAt = lastSlotEnteredAt;
        SlotEntrySequence = lastSlotEnteredSequence;

        // 收票锚点刻意**不**沿用旧时间轴：服务端重启 / 重建后不追补断线期错过的席位
        // （那时玩家无法举手），未收完的收票由说书人「继续收票」按新事件的记录时刻重锚。
        VoteSweepStartedAt = null;
        VoteSweepEntrySequence = lastSweepAnchorSequence;
        VoteSweepBallotKind = lastSweepKind;
        VoteSweepBallotIndex = lastSweepIndex;

        var pending = machine?.PendingRequest;
        if (pending is { Status: OperationRequestStatus.Pending } && pending.Id == lastIssuedRequestId)
        {
            PendingRequestSince = lastIssuedAt;
        }
    }

    /// <summary>清空全部跟踪量（事件流损坏、房间停在空状态时）。</summary>
    public void Clear()
    {
        _recentSeatChanges.Clear();
        _informationResults.Clear();
        _slotResolutions.Clear();
        _publicLife = PublicLifeBoard.Empty;
        _annotations = SeatAnnotationLedger.Empty;
        LastResolution = null;
        LastVoidedRequest = null;
        LastDepartureRuling = null;
        SlotStartedAt = null;
        SlotEntrySequence = null;
        ClearBallotAnchor();
        PendingRequestSince = null;
    }

    /// <summary>重锚钟盘时间轴并记下它属于哪条选票（提名 / 流放；D2 实施口径）。</summary>
    private void AnchorBallot(BallotKind kind, int index, DateTimeOffset recordedAt, long sequence)
    {
        VoteSweepStartedAt = recordedAt;
        VoteSweepEntrySequence = sequence;
        VoteSweepBallotKind = kind;
        VoteSweepBallotIndex = index;
    }

    /// <summary>收票锚点退场（收口 / 白天关闭 / 计划切换 / 状态清空）。</summary>
    private void ClearBallotAnchor()
    {
        VoteSweepStartedAt = null;
        VoteSweepEntrySequence = null;
        VoteSweepBallotKind = null;
        VoteSweepBallotIndex = null;
    }

    private static AbilityResolutionSnapshot ToSnapshot(AbilityResolvedEvent resolved, long sequence) =>
        new()
        {
            Actor = resolved.Actor,
            Ability = resolved.Ability,
            Effective = resolved.Effective,
            Malfunctions = resolved.Malfunctions,
            Note = resolved.Note,
            Sequence = sequence,
        };

    private static VoidedRequestSnapshot ToVoidSnapshot(OperationRequestVoidedEvent voided, long sequence) =>
        new()
        {
            Id = voided.RequestId,
            Reason = voided.Void.Reason,
            Note = voided.Void.Note,
            Sequence = sequence,
        };

    private void AppendInformationResult(InformationResultIssuedEvent information, long sequence)
    {
        _informationResults.Add((information.Recipient, new InformationResultSnapshot
        {
            Ability = information.Ability,
            Content = information.Content,
            Sequence = sequence,
        }));
    }

    private void AppendSeatChange(SeatStateChangedEvent seatChanged, long sequence, DateTimeOffset recordedAt)
    {
        _recentSeatChanges.Add(new SeatChangeSnapshot
        {
            Seat = seatChanged.Seat,
            Life = seatChanged.Life,
            Character = seatChanged.Character,
            Alignment = seatChanged.Alignment,
            Drunk = seatChanged.Drunk,
            Poison = seatChanged.Poison,
            Reason = seatChanged.Reason,
            CausedBy = seatChanged.CausedBy,
            EffectId = seatChanged.EffectId,
            Sequence = sequence,
            RecordedAt = recordedAt,
        });

        if (_recentSeatChanges.Count > RecentSeatChangeCapacity)
        {
            _recentSeatChanges.RemoveAt(0);
        }
    }
}
