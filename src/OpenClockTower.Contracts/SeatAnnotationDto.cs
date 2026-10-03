namespace OpenClockTower.Contracts;

/// <summary>
/// 一条说书人注记（D-0019）：魔典上挂在席位旁的**自由文本**提示标记。
/// </summary>
/// <remarks>
/// 只说书人视图下发；玩家投影里没有这条字段（D-0012 §4.3）。
/// 文本已在写入侧归一化（折叠空白、长度上限），前端仍按不可信输入做二次有界化（架构 §4.4）。
/// </remarks>
public sealed record SeatAnnotationDto
{
    /// <summary>签发标识（一局内唯一，只增不减）；改 / 删按它定位。</summary>
    public required int Id { get; init; }

    /// <summary>挂在哪一席。</summary>
    public required int Seat { get; init; }

    /// <summary>自由文本。</summary>
    public required string Text { get; init; }
}
