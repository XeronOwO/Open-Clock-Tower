using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 发给某个席位的信息类结果：**只含内容**。
/// </summary>
/// <remarks>
/// 「可能为假」标记刻意不进玩家投影：百科《重要细节》· 2026-10-01 抓取 · 三-1 要求
/// 「不要告诉玩家他醉酒或是中毒了！取而代之的是，让他们以清醒和健康的状态来执行行动」——
/// 把失效提示下发给玩家等于泄底。该标记只说书人视图可见。
/// </remarks>
public sealed record InformationResultSnapshot
{
    /// <summary>产生这条信息的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>说书人裁定的信息原文。</summary>
    public required string Content { get; init; }

    /// <summary>产生这条信息的事件序号。</summary>
    public required long Sequence { get; init; }
}
