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
                    break;
                case SlotEnteredEvent:
                    SlotStartedAt = recordedAt;
                    PendingRequestSince = null;
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
        SlotStartedAt = null;
        PendingRequestSince = null;

        DateTimeOffset? lastSlotEnteredAt = null;
        OperationRequestId? lastIssuedRequestId = null;
        DateTimeOffset? lastIssuedAt = null;

        foreach (var stored in storedEvents)
        {
            _publicLife = PublicLifeBoardFolder.Apply(_publicLife, stored.Event);
            switch (stored.Event)
            {
                case PhaseStartedEvent:
                    _slotResolutions.Clear();
                    break;
                case SlotEnteredEvent:
                    lastSlotEnteredAt = stored.RecordedAt;
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

                // 说书人注记（D-0019）：重启 / 重建按事件流恢复同一本账。
                case SeatAnnotationAddedEvent:
                case SeatAnnotationUpdatedEvent:
                case SeatAnnotationRemovedEvent:
                    _annotations = SeatAnnotationMachine.Apply(_annotations, stored.Event);
                    break;
            }
        }

        SlotStartedAt = lastSlotEnteredAt;

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
        SlotStartedAt = null;
        PendingRequestSince = null;
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
