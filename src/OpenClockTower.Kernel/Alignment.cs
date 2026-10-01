namespace OpenClockTower.Kernel;

/// <summary>
/// 阵营。与「角色」是两个**相互独立**的状态：善良玩家可以持有邪恶角色，反之亦然。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三-2——"如果一名玩家改变了阵营，他的角色会保持不变，反之亦然。"
/// 因此本类型与 <see cref="CharacterId"/> 之间**不得存在任何联合约束**。
/// </remarks>
public enum Alignment
{
    /// <summary>善良。</summary>
    Good,

    /// <summary>邪恶。</summary>
    Evil,
}
