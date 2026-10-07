using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 旅行者加入 / 离场 / 离场申请步骤（票据 `traveller-and-exile` D1 + 本批 D-0037）：
/// 复盘把四者呈现为原子事实步骤。
/// </summary>
/// <remarks>
/// 加入的六维度变化由同批的 <see cref="SeatStateChangedEvent"/> 另成一步（与方古侵染同款：
/// 事实与账各是一条原子步骤，D-0020 不合并）；这里只呈现"谁以什么身份入场 / 谁申请离开 /
/// 说书人怎么裁的 / 谁离场了"。复盘是终局后的完整事实面（R-0043），因此阵营在步骤详情里可见；
/// **实时公开面不含阵营**。离场申请是"玩家发起 → 说书人裁定"（D-0037）：申请与裁定各一步，
/// 复盘据此还原谁什么时候提的、说书人怎么裁的。
/// </remarks>
internal sealed class TravellerReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(TravellerJoinedEvent),
        typeof(TravellerDepartureRequestedEvent),
        typeof(TravellerDepartureResolvedEvent),
        typeof(TravellerDepartedEvent),
    ];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        TravellerJoinedEvent joined => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(joined.Seat)}：旅行者加入"
                + $"（{ReplayText.CharacterValue(joined.Character)}）",
            Detail = "公开宣告：席位 + 角色 + 能力；阵营不公开"
                + "（百科《旅行者》· 2026-10-04 抓取 · 旅行者运作方式第 6 步）。"
                + $"说书人私下裁定阵营：{ReplayText.Alignment(joined.Alignment)}。",
            Seats =
            [
                new ReplaySeatDelta
                {
                    Seat = joined.Seat,
                    Character = joined.Character,
                    Alignment = joined.Alignment,
                    Reason = "旅行者加入",
                },
            ],
            Markers =
            [
                new ReplayMarker
                {
                    Kind = "traveller-joined",
                    Seat = joined.Seat,
                    Text = ReplayText.CharacterValue(joined.Character),
                },
            ],
        },
        TravellerDepartureRequestedEvent requested => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(requested.Seat)}：旅行者提出离场申请（等说书人裁定）",
            Detail = string.IsNullOrWhiteSpace(requested.Note)
                ? "玩家发起 → 说书人裁定；批准后该席位才离场（D-0037）。"
                : $"旅行者说明：{requested.Note}；玩家发起 → 说书人裁定，批准后该席位才离场（D-0037）。",
            Seats = [new ReplaySeatDelta { Seat = requested.Seat, Reason = "申请离场" }],
            Markers = [new ReplayMarker { Kind = "traveller-departure-requested", Seat = requested.Seat }],
        },
        TravellerDepartureResolvedEvent resolved => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Decision,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(resolved.Seat)}：离场申请"
                + (resolved.Approved ? "被批准" : "被驳回"),
            Detail = string.IsNullOrWhiteSpace(resolved.Note)
                ? (resolved.Approved ? "批准：该席位随即离场。" : "驳回：该席位留在本局。")
                : (resolved.Approved ? $"批准，说书人说明：{resolved.Note}；该席位随即离场。" : $"驳回，说书人说明：{resolved.Note}；该席位留在本局。"),
            Seats = [],
            Markers =
            [
                new ReplayMarker
                {
                    Kind = resolved.Approved ? "traveller-departure-approved" : "traveller-departure-rejected",
                    Seat = resolved.Seat,
                },
            ],
        },
        TravellerDepartedEvent departed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(departed.Seat)}：旅行者离场（不再计入任何人数口径）",
            Detail = string.IsNullOrWhiteSpace(departed.Note)
                ? "席位与票据保留；离场后不计入流放分母 / 胜负 / 投票（rulings.md R-0044 第 6 条）。"
                : $"说书人说明：{departed.Note}；席位与票据保留，离场后不计入任何人数口径"
                    + "（rulings.md R-0044 第 6 条）。",
            Seats = [new ReplaySeatDelta { Seat = departed.Seat, Reason = "旅行者离场" }],
            Markers = [new ReplayMarker { Kind = "traveller-departed", Seat = departed.Seat }],
        },
        _ => throw new InvalidOperationException(
            $"TravellerReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };
}
