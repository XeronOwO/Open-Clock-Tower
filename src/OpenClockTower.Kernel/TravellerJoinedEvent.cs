namespace OpenClockTower.Kernel;

/// <summary>
/// 一名旅行者加入本局（新席位或本局尚未分配的席位）：获得角色与**说书人私下裁定的阵营**。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《旅行者》（2026-10-04 抓取 · 旅行者运作方式第 2–3、6 步）：说书人为旅行者选择阵营并私下告知；
/// 公开宣告「谁 + 角色 + 能力」，**不公布阵营**（公开面口径见票据 `traveller-and-exile` D7）。
/// </para>
/// <para>
/// 本事件是**事实**（回答"这名玩家以旅行者身份入场了"），公开宣告、复盘与投影由它驱动；
/// 六维度账（角色 / 阵营 / 生死 / 醉酒 / 中毒）由同批的 <see cref="SeatStateChangedEvent"/> 落地——
/// 与方古侵染「事实事件 + 配套账事件」同一条管法（D-0010：事件是唯一事实来源）。
/// 阵营只随事件流保存，玩家投影按白名单过滤（D-0012 §4.3）。
/// </para>
/// </remarks>
public sealed record TravellerJoinedEvent : GameEvent
{
    /// <summary>加入的席位（会话信息里的席位号）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>说书人给出的旅行者角色（花名册里的旅行者五选一）。</summary>
    public required CharacterId Character { get; init; }

    /// <summary>说书人私下裁定的阵营；不进任何公开投影。</summary>
    public required Alignment Alignment { get; init; }
}
