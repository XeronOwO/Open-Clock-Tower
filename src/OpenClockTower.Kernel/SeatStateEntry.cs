namespace OpenClockTower.Kernel;

/// <summary>
/// 状态账里的一行：某个席位**当前已知**的六维度状态，以及每个维度各自的归因。
/// </summary>
/// <remarks>
/// <para>
/// 六维度依据：百科《重要细节》三。五个维度各带自己的归因（值 / 原因 / 导致方），
/// 因此"这个玩家现在中毒，是被谁害的"可以只查中毒那一格，不会被之后的角色变化冲掉。
/// 「疯狂」刻意不在 <see cref="SeatState"/> 里（R-0003：引擎不判定疯狂），
/// 它以 <see cref="MadnessRequirement"/> 的形式单独挂在本行上，只由裁定写入。
/// </para>
/// <para>
/// 维度为 null = **该维度尚未被观测到**，不是"默认值"。六维度相互独立：
/// 只报生死不会顺带把角色写成某个值（<see cref="SeatState"/> 的硬约束）。
/// </para>
/// </remarks>
public sealed record SeatStateEntry
{
    /// <summary>席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>已知的生死及归因；null = 尚未观测。</summary>
    public StateFact<LifeState>? Life { get; init; }

    /// <summary>已知的角色及归因；null = 尚未观测。</summary>
    public StateFact<CharacterId>? Character { get; init; }

    /// <summary>已知的阵营及归因；null = 尚未观测。</summary>
    public StateFact<Alignment>? Alignment { get; init; }

    /// <summary>已知的醉酒状态及归因；null = 尚未观测。</summary>
    public StateFact<DrunkState>? Drunk { get; init; }

    /// <summary>已知的中毒状态及归因；null = 尚未观测。</summary>
    public StateFact<PoisonState>? Poison { get; init; }

    /// <summary>当前挂在该席位上的疯狂要求（只由裁定写入；引擎不判定）。</summary>
    public IReadOnlyList<MadnessRequirement> Madnesses { get; init; } = [];

    /// <summary>已知的生死值；null = 尚未观测。</summary>
    public LifeState? LifeValue => Life?.Value;

    /// <summary>已知的角色值；null = 尚未观测。</summary>
    public CharacterId? CharacterValue => Character?.Value;

    /// <summary>已知的醉酒值；null = 尚未观测。</summary>
    public DrunkState? DrunkValue => Drunk?.Value;

    /// <summary>已知的中毒值；null = 尚未观测。</summary>
    public PoisonState? PoisonValue => Poison?.Value;

    /// <summary>五个维度是否都已被观测——只有齐全时才能拼出完整 <see cref="SeatState"/>。</summary>
    public bool IsComplete =>
        Life is not null && Character is not null && Alignment is not null && Drunk is not null && Poison is not null;

    /// <summary>
    /// 拼出完整 <see cref="SeatState"/>；有维度未被观测时返回 null——**不猜**，缺哪维就说缺哪维。
    /// </summary>
    public SeatState? ToSeatState() => IsComplete
        ? new SeatState
        {
            Character = Character!.Value,
            Alignment = Alignment!.Value,
            Life = Life!.Value,
            Drunk = Drunk!.Value,
            Poison = Poison!.Value,
        }
        : null;
}
