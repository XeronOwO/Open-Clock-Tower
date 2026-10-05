namespace OpenClockTower.Kernel;

/// <summary>杂耍艺人的一条公开猜测：猜某个席位是某个角色（R-0057-B）。</summary>
/// <remarks>
/// 来源：百科《杂耍艺人》· 2026-10-01 抓取 · 角色能力——「在你的**首个白天**，你可以**公开**猜测任意玩家的
/// 角色**最多五次**。在**当晚**，你会得知猜测正确的角色数量。」；· 角色简介 2——玩家与角色都可以重复
/// （同一席位猜多个角色、多个席位猜同一角色都合法）。
/// </remarks>
public sealed record JugglerGuess
{
    /// <summary>被猜的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>猜的角色；可以是场上没有的角色——猜错本来就是这条能力的一部分。</summary>
    public required CharacterId Character { get; init; }
}
