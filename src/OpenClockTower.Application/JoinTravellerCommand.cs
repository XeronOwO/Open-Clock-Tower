using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人把一名旅行者加入本局（票据 `traveller-and-exile` D1；任意时刻，含首个阶段之前）。
/// </summary>
/// <remarks>
/// <para>
/// 加入有两种落点，由 <see cref="Seat"/> 区分：
/// ① 指定本局**尚未分配角色**的席位（15+ 开局提前占好的高号席）——只写事件，不动席位名单；
/// ② 不指定（null）——服务端追加一个新席位并签发新票据，由说书人转交给新到场的玩家。
/// </para>
/// <para>
/// 角色由说书人录入（玩家在桌面上选了自己的角色），阵营由说书人私下裁定（百科《旅行者》·
/// 2026-10-04 抓取 · 旅行者运作方式第 2 步）；六维度初始条件（存活 / 清醒 / 健康）由加入处分补全。
/// </para>
/// </remarks>
public sealed record JoinTravellerCommand : GameCommand
{
    /// <summary>目标席位；null = 追加新席位（服务端分配席位号并签发票据）。</summary>
    public SeatId? Seat { get; init; }

    /// <summary>旅行者角色（花名册旅行者五选一）。</summary>
    public required CharacterId Character { get; init; }

    /// <summary>说书人私下裁定的阵营；不进任何公开投影。</summary>
    public required Alignment Alignment { get; init; }

    /// <summary>
    /// 邪恶旅行者要得知的**存活恶魔**席位：说书人按局面选择告诉他一名或全部（百科《旅行者》）；
    /// 善良旅行者必须为空。
    /// </summary>
    public IReadOnlyList<SeatId> RevealDemonSeats { get; init; } = [];
}
