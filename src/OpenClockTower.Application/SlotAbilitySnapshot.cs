using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>本槽位能力判定摘要（说书人每步摘要用）。</summary>
public sealed record SlotAbilitySnapshot
{
    /// <summary>判定依据。</summary>
    public required SlotAbilityBasis Basis { get; init; }

    /// <summary>被判定 / 被结算的能力标识；取不到能力契约时为 null。</summary>
    public AbilityId? Ability { get; init; }

    /// <summary>是否正常生效；无法判定时为 null。</summary>
    public bool? Effective { get; init; }

    /// <summary>未正常生效时的原因分类（R-0004）；生效或无法判定时为 null。</summary>
    public MalfunctionKind? Malfunction { get; init; }

    /// <summary>说明（分类表达不了的组合写在这里）。</summary>
    public string? Note { get; init; }

    /// <summary>已结算结论的事件序号；预览与无法判定为 null。</summary>
    public long? Sequence { get; init; }
}
