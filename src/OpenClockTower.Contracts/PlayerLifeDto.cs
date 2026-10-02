namespace OpenClockTower.Contracts;

/// <summary>公开生死面上的一条事实：席位 + 对外可见 / 公告后的生死。</summary>
/// <remarks>
/// 依据 `docs/standard/rulings.md` R-0022：公开面只含"谁死 / 谁复活"，**不含死因**——
/// 因此这里刻意没有原因 / 归因 / 来源字段（说书人账目不下发，D-0012 §4.3）。
/// </remarks>
public sealed record PlayerLifeDto
{
    /// <summary>席位。</summary>
    public required int Seat { get; init; }

    /// <summary>生死状态：<c>Alive</c>（存活 / 复活）/ <c>Dead</c>（死亡）。</summary>
    public required string State { get; init; }
}
