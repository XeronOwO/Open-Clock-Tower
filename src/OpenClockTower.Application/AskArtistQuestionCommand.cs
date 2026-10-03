namespace OpenClockTower.Application;

/// <summary>
/// 玩家（艺术家）在白天主动向说书人提出一个是 / 否问题（R-0040）。
/// </summary>
/// <remarks>
/// 与操作请求方向相反：这条命令由玩家主动发出。席位**不在命令里自称**——
/// 由凭据推导（D-0012：客户端声明一律不可信）；问题文本原样进内核校验。
/// </remarks>
public sealed record AskArtistQuestionCommand : GameCommand
{
    /// <summary>问题全文（自由文本）。</summary>
    public required string Question { get; init; }
}
