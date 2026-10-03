namespace OpenClockTower.Kernel;

/// <summary>
/// 玩家（艺术家）在白天主动提出一个是 / 否问题（R-0040）。
/// </summary>
/// <remarks>
/// 与操作请求方向相反：这是玩家主动发起的输入，不是服务端推送的等待响应。
/// 规则依据：百科《艺术家》· 2026-10-01 抓取 · 运作方式 4「是否提问由艺术家决定，而非说书人」。
/// </remarks>
public sealed record AskArtistQuestionInput : StepMachineInput
{
    /// <summary>提问的席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>问题全文（自由文本）。</summary>
    public required string Question { get; init; }
}
