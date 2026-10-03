using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 「失去能力」标记的限次能力注册表（R-0040）：哪些能力用尽后需要在魔典上放提示标记。
/// </summary>
/// <remarks>
/// 平台口径：标记由**能力使用账本派生**（不新增第二份事实，D-0010）——这里只登记集合与文案，
/// 投影侧（Application）按（席位，能力）去重展示；玩家面只有"本人已用尽"的窄字段。
/// 新的限次能力实现时在这里登记（女裁缝 / 艺术家是首批）。
/// </remarks>
public static class OnceAbilities
{
    /// <summary>已登记的限次能力（顺序 = 呈现顺序）。</summary>
    public static IReadOnlyList<OnceAbility> Registry { get; } =
    [
        new() { Ability = new AbilityId("seamstress"), DisplayName = "女裁缝" },
        new() { Ability = new AbilityId("artist"), DisplayName = "艺术家" },
    ];

    /// <summary>该能力是不是登记在册的限次能力；不是时返回 null。</summary>
    public static OnceAbility? Find(AbilityId ability) =>
        Registry.FirstOrDefault(entry => entry.Ability == ability);
}
