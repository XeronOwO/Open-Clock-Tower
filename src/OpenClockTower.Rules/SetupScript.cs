using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 参与配板的剧本数据：四个角色池。求解器只认这份数据，合成剧本可以拿它测边界。
/// </summary>
public sealed record SetupScript(
    IReadOnlyList<SetupPoolEntry> Townsfolk,
    IReadOnlyList<SetupPoolEntry> Outsiders,
    IReadOnlyList<SetupPoolEntry> Minions,
    IReadOnlyList<SetupPoolEntry> Demons)
{
    /// <summary>按类型取池；花名册四个类型之外由调用方保证不出现。</summary>
    public IReadOnlyList<SetupPoolEntry> PoolOf(CharacterType type) => type switch
    {
        CharacterType.Townsfolk => Townsfolk,
        CharacterType.Outsider => Outsiders,
        CharacterType.Minion => Minions,
        CharacterType.Demon => Demons,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "配板只覆盖花名册的四个角色类型"),
    };
}
