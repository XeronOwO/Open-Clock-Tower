namespace OpenClockTower.Kernel;

/// <summary>
/// R-0004 的计数口径：失效账本里哪些分类计入数学家的数字。
/// </summary>
/// <remarks>
/// 依据 <c>docs/standard/rulings.md</c> R-0004：
/// **计入**中毒 / 醉酒 / 涡流，以及原因未定（<see cref="MalfunctionKind.Open"/>——它只表示原因待核对，
/// 不改变"确实失效"这一事实）；**不计入**相克规则（<see cref="MalfunctionKind.Jinx"/>）与
/// 能力自身设定（<see cref="MalfunctionKind.AbilityDesign"/>）；咖啡师与说书人裁定两条路径 R-0004 尚未核对，
/// 在补齐前**不计入**，也不得由引擎自行写这两类记录。
/// 数字按**玩家**去重、窗口为「上一个黎明到数学家被唤醒」，且数学家自身不计——前两条需要失效记录带白天号、
/// 第三条要在取值时排除数学家席位，随数学家角色实现（另票）。本类只回答"这个分类算不算"。
/// </remarks>
public static class MalfunctionCounting
{
    /// <summary>该分类是否计入数学家的数字（R-0004 逐条对表：未核对的一律不计入，不默认放行）。</summary>
    public static bool CountsForMathematician(this MalfunctionKind kind) =>
        kind is MalfunctionKind.Poisoned
            or MalfunctionKind.Drunk
            or MalfunctionKind.Vortox
            or MalfunctionKind.Open;
}
