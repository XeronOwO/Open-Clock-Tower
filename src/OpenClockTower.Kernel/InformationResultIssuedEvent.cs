namespace OpenClockTower.Kernel;

/// <summary>
/// 信息类能力的结果：**说书人裁定的内容**——平台只记录与转达，不判定真假（D-0002）。
/// </summary>
/// <remarks>
/// 依据：百科《重要细节》三-3——醉酒或中毒的玩家「其角色能力**可能**会获取到错误的信息」，
/// 给不给假信息、给什么信息都由说书人裁定。平台因此只携带 <see cref="MayBeFalse"/> 这一提示，
/// 绝不生成「这条信息为真 / 为假」的结论；它按收件人投影下发（D-0012 §4.3）。
/// </remarks>
public sealed record InformationResultIssuedEvent : GameEvent
{
    /// <summary>收件人席位——只有这一名玩家看得到（说书人视图不受限）。</summary>
    public required SeatId Recipient { get; init; }

    /// <summary>产生这条信息的能力。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>说书人裁定的信息原文。</summary>
    public required string Content { get; init; }

    /// <summary>平台提示：这条信息可能为假（能力未生效或能力自身设定）。</summary>
    public required bool MayBeFalse { get; init; }

    /// <summary>说明（例如「能力未生效（中毒）：信息由说书人裁定」）。</summary>
    public string? Note { get; init; }
}
