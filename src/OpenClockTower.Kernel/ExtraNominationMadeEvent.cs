namespace OpenClockTower.Kernel;

/// <summary>屠夫窗口里的额外提名（百科《屠夫》；R-0050；折进当天提名账）。</summary>
/// <remarks>
/// <para>
/// 与 <see cref="NominationMadeEvent"/> 共用提名序号与收票流程，但受《屠夫》明文豁免：
/// 不占发起人当日提名次数、可提名当天已被提名过的玩家（R-0050 第 3 条）。
/// </para>
/// <para>
/// 受理条件由 <see cref="ExtraNominationMachine.Nominate"/> 判：窗口开着、发起人是窗口授予席位、
/// 提名者在局存活、被提名者在局、同一时间至多一项提名。发起人身份由服务端从凭据推导。
/// </para>
/// </remarks>
public sealed record ExtraNominationMadeEvent : GameEvent
{
    /// <summary>所属白天。</summary>
    public required int DayNumber { get; init; }

    /// <summary>当天第几次提名（从 1 起，与常规提名共用一个序号序列；进事件流后稳定）。</summary>
    public required int NominationIndex { get; init; }

    /// <summary>发起提名的席位（窗口授予席位本人）。</summary>
    public required SeatId Nominator { get; init; }

    /// <summary>被提名的席位（可以是当天已被提名过的玩家）。</summary>
    public required SeatId Nominee { get; init; }

    /// <summary>提名发生时的提名者角色快照；未观测为 null（不猜；城镇公告员按它推演，R-0037）。</summary>
    public CharacterId? NominatorCharacter { get; init; }
}
