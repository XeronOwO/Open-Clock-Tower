namespace OpenClockTower.Application;

/// <summary>一局的**对外摘要**：大厅列表用，只含"玩家挑桌时需要知道的东西"。</summary>
/// <remarks>
/// 刻意不含票据与席位细节（那是入座之后的事）；也不含进行中的游戏内事实
/// （那属于视图投影，见 D-0012）。
/// </remarks>
public sealed record GameSummary
{
    /// <summary>游戏标识。</summary>
    public required GameId GameId { get; init; }

    /// <summary>桌名（玩家可见；未命名为空串）。</summary>
    public required string Name { get; init; }

    /// <summary>席位数。</summary>
    public required int SeatCount { get; init; }

    /// <summary>是否邀请制（邀请制桌不接受自助入座，要凭邀请码；D-0037）。</summary>
    public required bool IsInviteOnly { get; init; }
}
