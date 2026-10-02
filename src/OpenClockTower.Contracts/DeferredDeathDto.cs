namespace OpenClockTower.Contracts;

/// <summary>一条待定的死亡：麻脸巫婆之夜由说书人裁定确认或阻止（R-0030）。</summary>
public sealed record DeferredDeathDto
{
    /// <summary>被攻击的席位。</summary>
    public required int Target { get; init; }

    /// <summary>发起击杀的恶魔席位。</summary>
    public required int Source { get; init; }

    /// <summary>发起击杀的能力标识。</summary>
    public required string Ability { get; init; }

    /// <summary>发生位置与依据说明（说书人视图用）。</summary>
    public required string Note { get; init; }

    /// <summary>
    /// 说书人「确认」时的结果：true = 按**转化**结算（方古侵染：外来者变成新的邪恶方古、原方古死亡，
    /// 被攻击者本身不死亡），false = 普通死亡。口径见 <c>docs/standard/rulings.md</c> R-0030 / R-0034。
    /// </summary>
    public bool Transformation { get; init; }
}
