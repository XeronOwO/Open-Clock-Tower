namespace OpenClockTower.Contracts;

/// <summary>本槽位能力判定：已结算结论或按当前账的预览（说书人每步摘要用）。</summary>
public sealed record SlotAbilityDto
{
    /// <summary>判定依据：Settled（已结算）/ Preview（按当前账预览）/ Unknown（账不全，无法判定）。</summary>
    public required string Basis { get; init; }

    /// <summary>被判定 / 被结算的能力标识；取不到能力契约时为 null。</summary>
    public string? Ability { get; init; }

    /// <summary>是否正常生效；无法判定时为 null。</summary>
    public bool? Effective { get; init; }

    /// <summary>未正常生效时的原因分类（R-0004）；生效或无法判定时为 null。</summary>
    public string? Malfunction { get; init; }

    /// <summary>说明（分类表达不了的组合写在这里，例如「同时中毒且醉酒」）。</summary>
    public string? Note { get; init; }

    /// <summary>已结算结论的事件序号；预览与无法判定为 null。</summary>
    public long? Sequence { get; init; }
}
