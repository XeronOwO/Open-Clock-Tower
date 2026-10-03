using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>初始设置的分布计数：镇民 / 外来者 / 爪牙 / 恶魔。</summary>
public sealed record SetupCounts(int Townsfolk, int Outsiders, int Minions, int Demons)
{
    /// <summary>总数（合法配板必须等于玩家人数）。</summary>
    public int Total => Townsfolk + Outsiders + Minions + Demons;

    /// <summary>按类型取数量；花名册四个类型之外由调用方保证不出现（出现即数据缺陷，不猜）。</summary>
    public int Of(CharacterType type) => type switch
    {
        CharacterType.Townsfolk => Townsfolk,
        CharacterType.Outsider => Outsiders,
        CharacterType.Minion => Minions,
        CharacterType.Demon => Demons,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "分布计数只覆盖花名册的四个角色类型"),
    };
}
