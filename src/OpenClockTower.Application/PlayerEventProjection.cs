using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 把事件流按**接收者**投影成"玩家可见事件"：不该看见的事件根本不出现在结果里。
/// </summary>
/// <remarks>
/// 依据 D-0012 §4.3：信息只在**下发方向**校验，越权信息不下发，而不是靠前端不显示。
/// 因此这里是白名单：只有公开阶段与"发给这名席位"的请求 / 响应 / 作废能通过，
/// 其余（他人请求、状态账、槽位与进度）一律落在默认分支上被丢掉。
/// </remarks>
public static class PlayerEventProjection
{
    /// <summary>建立"请求标识 → 收件人"查表，用于判断某条响应 / 作废是谁的。</summary>
    public static Dictionary<string, SeatId> AddresseeLookup(IReadOnlyList<StoredEvent> storedEvents)
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

    /// <summary>把一条事件投影成某个席位可见的玩家事件；该席位不该看见时返回 null。</summary>
    public static PlayerEvent? ForSeat(
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

            // 旅行者加入 / 离场是公开事实：所有席位都收到同一份（只含席位与角色，阵营不下发——
            // 百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式第 6 步）。
            TravellerJoinedEvent joined => new PlayerEvent
            {
                Sequence = stored.Sequence,
                Kind = PlayerEventKind.TravellerJoined,
                Seat = joined.Seat,
                Character = joined.Character,
            },
            TravellerDepartedEvent departed => new PlayerEvent
            {
                Sequence = stored.Sequence,
                Kind = PlayerEventKind.TravellerDeparted,
                Seat = departed.Seat,
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
            InformationResultIssuedEvent information when information.Recipient == seat => new PlayerEvent
            {
                Sequence = stored.Sequence,
                Kind = PlayerEventKind.InformationResultIssued,
                Information = information,
            },
            _ => null,
        };

    private static bool IsRequestOfSeat(
        OperationRequestId requestId,
        SeatId seat,
        IReadOnlyDictionary<string, SeatId> addressees) =>
        addressees.TryGetValue(requestId.Value, out var addressee) && addressee == seat;
}
