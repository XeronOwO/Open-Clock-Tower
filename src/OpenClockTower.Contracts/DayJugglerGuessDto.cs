namespace OpenClockTower.Contracts;

/// <summary>一次公开猜测的线上形状（R-0057-B）：谁猜的（席位）、猜了哪几条。</summary>
/// <remarks>猜测是**公开事实**：这份形状同时进玩家端白天视图与说书人视图（R-0057-B 第 2 条）。</remarks>
public sealed record DayJugglerGuessDto
{
    /// <summary>猜测者席位。</summary>
    public required int Seat { get; init; }

    /// <summary>猜测内容（0–5 条，按提交顺序）。</summary>
    public required JugglerGuessDto[] Guesses { get; init; }
}
