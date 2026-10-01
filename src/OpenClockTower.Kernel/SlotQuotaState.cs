namespace OpenClockTower.Kernel;

/// <summary>
/// 当前槽位的最短时间配额状态。
/// </summary>
/// <remarks>
/// 依据 D-0013 §2/§4：每个槽位有固定配额，由服务端节拍器统一驱动；
/// 玩家秒回、请求被作废或代填，都**不得缩短**该槽位剩余配额。
/// </remarks>
public enum SlotQuotaState
{
    /// <summary>配额还在走：即使请求已了结也不能推进。</summary>
    Running,

    /// <summary>配额已走完：若没有挂起，就可以推进；若仍有挂起，继续等（D-0011 无超时）。</summary>
    Elapsed,
}
