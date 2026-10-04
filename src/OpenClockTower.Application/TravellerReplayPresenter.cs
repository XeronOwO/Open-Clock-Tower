using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 旅行者加入 / 离场步骤（票据 `traveller-and-exile` D1）：复盘把两者呈现为原子事实步骤。
/// </summary>
/// <remarks>
/// 加入的六维度变化由同批的 <see cref="SeatStateChangedEvent"/> 另成一步（与方古侵染同款：
/// 事实与账各是一条原子步骤，D-0020 不合并）；这里只呈现"谁以什么身份入场 / 谁离场了"。
/// 复盘是终局后的完整事实面（R-0043），因此阵营在步骤详情里可见；**实时公开面不含阵营**。
/// </remarks>
internal sealed class TravellerReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(TravellerJoinedEvent),
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
        },
        _ => throw new InvalidOperationException(
            $"TravellerReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };
}
