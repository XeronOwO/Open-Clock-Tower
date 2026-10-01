namespace OpenClockTower.Kernel;

/// <summary>
/// 生死。任意时间点一名玩家必居其一。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三-1——"在任意时间点，一名玩家一定会处于存活或死亡两种状态之一。"
/// 与之配套的两条易错点记录在案：**处决 ≠ 死亡**（可被处决而不死），
/// 且**已死亡的玩家无法再次死亡**。
/// </remarks>
public enum LifeState
{
    /// <summary>存活。</summary>
    Alive,

    /// <summary>死亡。</summary>
    Dead,
}
