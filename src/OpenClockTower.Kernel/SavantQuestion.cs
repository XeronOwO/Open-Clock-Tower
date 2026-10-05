namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者的进行中提问：白天私下向说书人要两条信息（一真一假），等待裁定。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《博学者》· 2026-10-01 抓取 · 角色能力——「每个白天，你可以私下询问说书人以得知两条信息：
/// 一个是正确的，一个是错误的。」；· 角色简介——说书人选两条、博学者不知道哪条对哪条、
/// 可以选择不要、醉酒 / 中毒时可能两条全对或全错。
/// </para>
/// <para>
/// 与 <see cref="ArtistQuestion"/> 的分工：那个带问题全文（是 / 否问题由玩家提），这个**不带问题**——
/// 内容完全由说书人给；两者都只属于当前白天，结清后清空。
/// </para>
/// </remarks>
public sealed record SavantQuestion
{
    /// <summary>提问的博学者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>
    /// 提问时刻该席位的角色（来源检索键）：结清按它取契约——挂起期间角色被换走也不改变结清路径
    /// （与艺术家提问 / 槽位结算按槽位 <c>Owner</c> 取契约同姿态）。
    /// </summary>
    public required CharacterId Character { get; init; }
}
