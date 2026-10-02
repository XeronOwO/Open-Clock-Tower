namespace OpenClockTower.Kernel;

/// <summary>
/// 待定死亡确认后**不发生死亡**、改为「角色 + 阵营转变 + 来源死亡」的转化载荷（方古侵染外来者）。
/// </summary>
/// <remarks>
/// <para>
/// 依据：百科《方古》· 2026-10-01 抓取 · 角色简介 2 / 运作方式 11–14——「方古首次攻击并成功杀死外来者时，
/// 改为方古死亡，外来者变成邪恶的方古」；· 提示标记「限一次」。
/// </para>
/// <para>
/// 它为什么挂在待定死亡上：麻脸巫婆之夜的死亡裁量窗口把当夜所有恶魔击杀都变成待定死亡
/// （<c>docs/standard/rulings.md</c> R-0030），而方古这次攻击的「自然结果」本身就是转化而非死亡。
/// 平台口径见 R-0034：说书人「确认」= 按能力转化，「阻止」= 击杀与转化都不发生。
/// </para>
/// </remarks>
public sealed record DeferredTransformation
{
    /// <summary>被侵染的席位（外来者 → 新的邪恶方古）。</summary>
    public required SeatId Target { get; init; }

    /// <summary>转化后的角色。</summary>
    public required CharacterId Character { get; init; }

    /// <summary>转化后的阵营。</summary>
    public required Alignment Alignment { get; init; }

    /// <summary>转化后死亡的席位（原方古）。</summary>
    public required SeatId Dies { get; init; }

    /// <summary>转化说明（进审计与说书人视图）。</summary>
    public required string Note { get; init; }
}
