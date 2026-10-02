namespace OpenClockTower.Contracts;

/// <summary>状态账里的一行：某个席位**当前已知**的维度与逐维度归因（说书人视角）。</summary>
/// <remarks>
/// 未观测的维度不出现在 <see cref="Facts"/> 里——"没观测到"与"取默认值"是两回事。
/// </remarks>
public sealed record SeatStateDto
{
    /// <summary>席位号。</summary>
    public required int Seat { get; init; }

    /// <summary>已观测的维度事实，按生死 / 角色 / 阵营 / 醉酒 / 中毒排列。</summary>
    public required SeatStateFactDto[] Facts { get; init; }

    /// <summary>
    /// 当前挂在该席位上的疯狂要求（**只含未撤下的**；由能力产生，R-0021；引擎不判定是否疯狂，R-0003）。
    /// 这里只下发给说书人的「要证明的角色」：来源 / 期限 / 撤下事实留在账本与事件流里，
    /// 说书人面板暂不单独呈现（见票据残余）。
    /// </summary>
    public required string[] Madnesses { get; init; }
}
