namespace OpenClockTower.Kernel;

/// <summary>
/// 一条「疯狂地证明自己是 X」的要求：谁被要求、证明什么、谁的能力在要求、有效到哪一天。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/standard/rulings.md</c> R-0003 与 R-0021：疯狂**不是**引擎状态——引擎不判定玩家是否疯狂，
/// 因此它不出现在 <see cref="SeatState"/> 里；账上只记录「谁在何时被要求证明什么」。
/// 产生方是能力（洗脑师夜晚行动生效时写入，R-0021），是否疯狂仍由说书人裁定（据此处罚处决，R-0020）。
/// </para>
/// <para>
/// **存续窗口**（R-0021）：施加夜的次日白天与紧随其后的夜晚有效，在下一个黎明到期撤下；
/// 来源死亡或换角色时立即终止——与 <see cref="PersistentEffect"/> 走同一条折叠传播，
/// 不另开一套生命周期账（D-0015：一个写入方）。
/// </para>
/// </remarks>
public sealed record MadnessRequirement
{
    /// <summary>要求的稳定标识（<c>{PlanLabel}:{SlotId}:madness</c>）：重放与撤下按它认人。</summary>
    public required MadnessRequirementId Id { get; init; }

    /// <summary>被要求疯狂证明什么的玩家席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>被要求疯狂证明的内容（角色显示名，如「博学者」）。</summary>
    public required string ProveToBe { get; init; }

    /// <summary>要求来自哪个席位的能力。</summary>
    public required SeatId Source { get; init; }

    /// <summary>施加时来源的角色：来源换角色 = 原角色能力不在，要求立即终止（同 PersistentEffect）。</summary>
    public required CharacterId SourceCharacter { get; init; }

    /// <summary>产生这条要求的能力（归因与说书人视图用）。</summary>
    public required AbilityId Ability { get; init; }

    /// <summary>
    /// 到期日：第几天白天开始（<see cref="DayStartedEvent.DayNumber"/>）时撤下；
    /// null = 不按天到期（当前没有这种产生方，留下这条形状以免把"有期限"写死进模型）。
    /// </summary>
    public int? ExpiresAtDay { get; init; }

    /// <summary>终止事实（原因分类 + 说明 + 导致方）；null = 仍在账上。终止不可逆。</summary>
    public EffectTermination? Termination { get; init; }

    /// <summary>是否已经被撤下。</summary>
    public bool IsTerminated => Termination is not null;

    /// <summary>撤下本要求；撤下必须带原因。</summary>
    /// <param name="termination">撤下原因（分类 + 说明 + 导致方）。</param>
    public MadnessRequirement Terminate(EffectTermination termination)
    {
        ArgumentNullException.ThrowIfNull(termination);
        return this with { Termination = termination };
    }
}
