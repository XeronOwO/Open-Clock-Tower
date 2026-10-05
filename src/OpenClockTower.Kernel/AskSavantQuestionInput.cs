namespace OpenClockTower.Kernel;

/// <summary>
/// 「博学者向说书人要两条信息」这条白天玩家命令的内核输入。
/// </summary>
/// <remarks>
/// 与 <see cref="AskArtistQuestionInput"/> 的差别：博学者没有要问的问题——他只是在白天开口要两条信息，
/// 内容由说书人给（百科《博学者》· 2026-10-01 抓取 · 角色能力 / 角色简介）。
/// 席位由凭据推导，命令面不自称身份（D-0012）。
/// </remarks>
public sealed record AskSavantQuestionInput : StepMachineInput
{
    /// <summary>提问的席位（由凭据推导）。</summary>
    public required SeatId Seat { get; init; }
}
