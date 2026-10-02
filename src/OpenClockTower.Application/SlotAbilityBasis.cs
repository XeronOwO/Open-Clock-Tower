namespace OpenClockTower.Application;

/// <summary>每步摘要里能力判定的依据：已结算 / 按当前账预览 / 无法判定。</summary>
/// <remarks>
/// 预览不是猜测：它用与结算同一个 <c>AbilityEffectivenessEvaluator</c> 判定当前账；
/// 账不全时给 <see cref="Unknown"/>，绝不映射成默认值（D-0015）。
/// </remarks>
public enum SlotAbilityBasis
{
    /// <summary>本槽位已经产出过能力结算事件，结论就是它。</summary>
    Settled,

    /// <summary>还没有结算，按当前状态账给出「如果此刻结算」的结论。</summary>
    Preview,

    /// <summary>账不全（生死 / 醉酒 / 中毒未观测齐），无法判定。</summary>
    Unknown,
}
