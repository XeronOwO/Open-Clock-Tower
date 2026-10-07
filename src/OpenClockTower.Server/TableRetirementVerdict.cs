namespace OpenClockTower.Server;

/// <summary>
/// 一次清扫里，**一张桌的判定结果**（M5 / G-A5-5 容量半边）。
/// </summary>
/// <remarks>
/// 四档就是全部可能，不多不少：到期该回收、有人在用所以不动、没有依据所以不判、
/// 有依据但还没到期。加第五档（比如"稍后再看"）的意思一定是某处少给了一个事实。
/// </remarks>
public enum TableRetirementVerdict
{
    /// <summary>已过保留期：该回收（`apply` 为假时只进报告，不动库）。</summary>
    Retire,

    /// <summary>这一桌当前有在线连接（席位或主持台）——**正在被使用，一律不动**。</summary>
    InUse,

    /// <summary>没有任何可判定的时间依据（老库里的空桌）——**没有依据就不删**。</summary>
    Undecidable,

    /// <summary>有依据，但还没到保留期。</summary>
    WithinRetention,
}
