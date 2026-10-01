namespace OpenClockTower.Kernel;

/// <summary>
/// 醉酒。与「中毒」是两件独立的事，**互不抵消**。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三-3——"醉酒与中毒状态不会互相抵消。
/// 一名醉酒的玩家中毒并不意味着他会因此恢复清醒和健康！"
/// 另见同页："无论玩家存活还是死亡，玩家都能醉酒或中毒。"
/// </remarks>
public enum DrunkState
{
    /// <summary>清醒。</summary>
    Sober,

    /// <summary>醉酒。</summary>
    Drunk,
}
