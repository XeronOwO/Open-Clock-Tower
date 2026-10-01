using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 会话级派生跟踪器：最近状态变化、当前槽位起算时刻、当前请求挂起时刻。
/// </summary>
/// <remarks>
/// <para>
/// 三者都是**由事件流派生**的，不是新的事实来源：重启恢复走 <see cref="Recover"/>，
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

    /// <summary>当前槽位的起算时刻；没有起点信息（异常数据）时为 null，此时宁可不动。</summary>
    public DateTimeOffset? SlotStartedAt { get; private set; }

    /// <summary>当前挂起请求的起算时刻；没有挂起时为 null（说书人视图据此算"卡了多久"）。</summary>
    public DateTimeOffset? PendingRequestSince { get; private set; }

    /// <summary>最近的状态变化（最新在后）；说书人视图的"刚发生了什么"。</summary>
    public IReadOnlyList<SeatChangeSnapshot> RecentSeatChanges => [.. _recentSeatChanges];

    /// <summary>把一批**已提交**的事件记进跟踪器。</summary>
    /// <param name="drafts">刚提交的事件草案（带序号与发生时刻）。</param>
    /// <param name="recordedAt">宿主记录的发生时刻。</param>
    public void Update(IReadOnlyList<StoredEventDraft> drafts, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(drafts);

        foreach (var draft in drafts)
        {
            switch (draft.Event)
            {
                case SlotEnteredEvent:
                    SlotStartedAt = recordedAt;
                    PendingRequestSince = null;
                    break;
                case OperationRequestIssuedEvent:
                    PendingRequestSince = recordedAt;
                    break;
                case OperationRequestAnsweredEvent:
                case OperationRequestVoidedEvent:
                case SlotAdvancedEvent:
                case SlotForceAdvancedEvent:
                    PendingRequestSince = null;
                    break;
                case SeatStateChangedEvent seatChanged:
                    AppendSeatChange(seatChanged, draft.Sequence, recordedAt);
                    break;
            }
        }
    }

    /// <summary>从事件流重建（服务端重启恢复 / 房间重建）。</summary>
    /// <param name="storedEvents">全量事件。</param>
    /// <param name="machine">重放得到的步骤机状态；用它判断"上一条请求是否仍挂着"。</param>
    public void Recover(IReadOnlyList<StoredEvent> storedEvents, StepMachineState? machine)
    {
        ArgumentNullException.ThrowIfNull(storedEvents);

        _recentSeatChanges.Clear();
        SlotStartedAt = null;
        PendingRequestSince = null;

        DateTimeOffset? lastSlotEnteredAt = null;
        OperationRequestId? lastIssuedRequestId = null;
        DateTimeOffset? lastIssuedAt = null;

        foreach (var stored in storedEvents)
        {
            switch (stored.Event)
            {
                case SlotEnteredEvent:
                    lastSlotEnteredAt = stored.RecordedAt;
                    break;
                case OperationRequestIssuedEvent issued:
                    lastIssuedRequestId = issued.Request.Id;
                    lastIssuedAt = stored.RecordedAt;
                    break;
                case SeatStateChangedEvent seatChanged:
                    AppendSeatChange(seatChanged, stored.Sequence, stored.RecordedAt);
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
        SlotStartedAt = null;
        PendingRequestSince = null;
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
            Sequence = sequence,
            RecordedAt = recordedAt,
        });

        if (_recentSeatChanges.Count > RecentSeatChangeCapacity)
        {
            _recentSeatChanges.RemoveAt(0);
        }
    }
}
