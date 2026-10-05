namespace OpenClockTower.Kernel;

/// <summary>「近期活动账」里的一条活动：谁、发生了什么、为什么（原因原样来自事件）。</summary>
public sealed record SeatActivity
{
    /// <summary>活动分类。</summary>
    public required SeatActivityKind Kind { get; init; }

    /// <summary>涉及的席位（角色 / 阵营变化与死亡都是这个席位本人）。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>事件里记的原因（如「被恶魔击杀」）；null = 事件没给原因。</summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 这条活动**变成**的角色：只有角色变化 / 首次观测带它（R-0057-B 的「首个白天」起算要用），
    /// 其余分类为 null。
    /// </summary>
    public CharacterId? Character { get; init; }

    /// <summary>发生时**已经开始的白天数**（0 = 还没有白天）——窗口口径见 R-0057-C 第 6 条。</summary>
    public required int DayNumber { get; init; }

    /// <summary>发生时是否夜里正在进行（开夜之后、黎明之前）。</summary>
    public required bool DuringNight { get; init; }
}
