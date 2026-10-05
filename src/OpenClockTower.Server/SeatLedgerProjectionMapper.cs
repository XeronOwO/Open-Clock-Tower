using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 状态账与效果的翻译：记录面（六维度 + 归因、效果链、状态变化）→ wire 形状。
/// </summary>
/// <remarks>
/// 从 <see cref="ProjectionMapper"/> 拆出：那一层是"各种视图 → DTO"的总入口，
/// 状态账这一族的字段映射（含逐维度归因的展开）自成一类，混在一起会把总入口撑到架构上限。
/// </remarks>
public static class SeatLedgerProjectionMapper
{
    /// <summary>状态账一行 → DTO：只列已观测的维度，未观测的维度不出现。</summary>
    public static SeatStateDto ToDto(SeatStateEntry entry)
    {
        var facts = new List<SeatStateFactDto>(capacity: 5);
        AddFact(facts, "Life", entry.Life);
        AddFact(facts, "Character", entry.Character);
        AddFact(facts, "Alignment", entry.Alignment);
        AddFact(facts, "Drunk", entry.Drunk);
        AddFact(facts, "Poison", entry.Poison);

        return new SeatStateDto
        {
            Seat = entry.Seat.Value,
            Facts = [.. facts],

            // 只下发**未撤下**的要求：实体游戏里标记到期 / 来源失效就移除了（R-0021）；
            // 已撤下的事实留在事件流与审计里，不在牌面上留幽灵标记。
            Madnesses = [.. entry.Madnesses
                .Where(requirement => !requirement.IsTerminated)
                .Select(requirement => requirement.ProveToBe)],
        };
    }

    /// <summary>持续型效果 → DTO（含终止原因；未终止时终止字段为空）。</summary>
    public static EffectDto ToDto(PersistentEffect effect) => new()
    {
        EffectId = effect.Id.Value,
        Kind = "Persistent",
        Ability = effect.Ability.Value,
        Source = effect.Source.Value,
        Target = effect.Target.Value,
        SourceCharacter = effect.SourceCharacter.Value,
        GrantedCharacter = effect.GrantedCharacter?.Value,
        Window = effect.Window?.ToString(),
        Terminated = effect.IsTerminated,
        TerminationKind = effect.Termination?.Kind.ToString(),
        TerminationReason = effect.Termination?.Reason,
        TerminationCausedBy = effect.Termination?.CausedBy?.Value,
    };

    /// <summary>即时型效果 → DTO（即时型不回滚，因此没有终止字段）。</summary>
    public static EffectDto ToDto(InstantaneousEffect effect) => new()
    {
        EffectId = effect.Id.Value,
        Kind = "Instantaneous",
        Ability = effect.Ability.Value,
        Source = effect.Source.Value,
        Target = effect.Target.Value,
        Terminated = false,
    };

    /// <summary>状态变化记录 → DTO。</summary>
    public static SeatChangeDto ToDto(SeatChangeSnapshot change) => new()
    {
        Seat = change.Seat.Value,
        Life = change.Life?.ToString(),
        Character = change.Character?.ToString(),
        Alignment = change.Alignment?.ToString(),
        Drunk = change.Drunk?.ToString(),
        Poison = change.Poison?.ToString(),
        Reason = change.Reason,
        CausedBy = change.CausedBy?.Value,
        EffectId = change.EffectId?.Value,
        Sequence = change.Sequence,
        RecordedAt = change.RecordedAt,
    };

    private static void AddFact<T>(List<SeatStateFactDto> facts, string dimension, StateFact<T>? fact)
        where T : struct
    {
        if (fact is null)
        {
            return;
        }

        facts.Add(new SeatStateFactDto
        {
            Dimension = dimension,
            Value = fact.Value.ToString() ?? string.Empty,
            Reason = fact.Reason,
            CausedBy = fact.CausedBy?.Value,
            EffectId = fact.EffectId?.Value,
        });
    }
}
