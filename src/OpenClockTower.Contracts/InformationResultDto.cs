namespace OpenClockTower.Contracts;

/// <summary>
/// 发给某个玩家的信息类结果：**只有内容**。
/// </summary>
/// <remarks>
/// 「可能为假」标记刻意不下发：百科《重要细节》· 2026-10-01 抓取 · 三-1 要求
/// 「不要告诉玩家他醉酒或是中毒了」——把失效提示推给玩家等于泄底。该标记只说书人视角可见。
/// </remarks>
public sealed record InformationResultDto
{
    /// <summary>产生这条信息的能力 slug。</summary>
    public required string Ability { get; init; }

    /// <summary>说书人裁定的信息原文。</summary>
    public required string Content { get; init; }
}
