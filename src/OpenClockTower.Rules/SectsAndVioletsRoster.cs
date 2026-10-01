using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 《梦殒春宵》首版 25 个角色的花名册（角色分配与建表的合法性依据）。
/// </summary>
/// <remarks>
/// 来源：<c>docs/standard/terminology.md</c> §9「首版角色清单」· 2026-10-01 已核对——
/// 25 个角色与百科《梦殒春宵》剧本页、各角色页「角色信息」节交叉比对一致。
/// 顺序即术语表顺序；只用于检索与校验，不承载结算语义。
/// </remarks>
public static class SectsAndVioletsRoster
{
    /// <summary>全部 25 个角色。</summary>
    public static IReadOnlyList<CharacterId> All { get; } = Array.AsReadOnly<CharacterId>(
    [
        new("clockmaker"),
        new("dreamer"),
        new("snake-charmer"),
        new("mathematician"),
        new("flowergirl"),
        new("town-crier"),
        new("oracle"),
        new("savant"),
        new("seamstress"),
        new("philosopher"),
        new("artist"),
        new("juggler"),
        new("sage"),
        new("mutant"),
        new("sweetheart"),
        new("barber"),
        new("klutz"),
        new("evil-twin"),
        new("witch"),
        new("cerenovus"),
        new("pit-hag"),
        new("fang-gu"),
        new("vigormortis"),
        new("no-dashii"),
        new("vortox"),
    ]);

    /// <summary>该角色是否在首版花名册里。</summary>
    public static bool Contains(CharacterId character) => All.Contains(character);
}
