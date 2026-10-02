namespace OpenClockTower.Contracts;

/// <summary>
/// 一条效果的归因链：谁施加、用哪个能力、作用于谁、是否已终止及终止原因（说书人视角）。
/// </summary>
public sealed record EffectDto
{
    /// <summary>效果标识。</summary>
    public required string EffectId { get; init; }

    /// <summary>效果类型：Persistent / Instantaneous。</summary>
    public required string Kind { get; init; }

    /// <summary>产生这条效果的能力。</summary>
    public required string Ability { get; init; }

    /// <summary>施加者席位。</summary>
    public required int Source { get; init; }

    /// <summary>作用对象席位。</summary>
    public required int Target { get; init; }

    /// <summary>施加时来源的角色——来源换角色即失去原能力，该效果随之终止；即时型效果没有这一项。</summary>
    public string? SourceCharacter { get; init; }

    /// <summary>
    /// 「获得能力」类效果（哲学家）被获得的角色；null = 普通效果。
    /// 口径见 <c>docs/standard/rulings.md</c> R-0036。
    /// </summary>
    public string? GrantedCharacter { get; init; }

    /// <summary>是否已终止（终止不可逆）。</summary>
    public required bool Terminated { get; init; }

    /// <summary>
    /// 终止原因分类：SourceDied / SourceLostAbility / StorytellerVoided / NoLongerApplies；
    /// 未终止时为空。
    /// </summary>
    public string? TerminationKind { get; init; }

    /// <summary>终止说明；未终止时为空。</summary>
    public string? TerminationReason { get; init; }

    /// <summary>导致终止的席位；未见得有人可归因时为空。</summary>
    public int? TerminationCausedBy { get; init; }
}
