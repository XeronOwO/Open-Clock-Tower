using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 胜负判定的角色事实端口实现（<see cref="IWinConditionFacts"/>）：读《梦殒春宵》花名册与三个角色标识。
/// </summary>
/// <remarks>
/// 无状态、无 IO，可以共享一个实例；角色数据仍然只有花名册一个来源（术语表 §9）。
/// </remarks>
public sealed class WinConditionFacts : IWinConditionFacts
{
    /// <summary>进程内共享实例。</summary>
    public static readonly WinConditionFacts Instance = new();

    private static readonly CharacterId Vortox = new("vortox");
    private static readonly CharacterId Klutz = new("klutz");

    private WinConditionFacts()
    {
    }

    /// <inheritdoc />
    public bool IsDemon(CharacterId character) =>
        SectsAndVioletsRoster.TypeOf(character) == CharacterType.Demon;

    /// <inheritdoc />
    public bool IsVortox(CharacterId character) => character == Vortox;

    /// <inheritdoc />
    public bool IsKlutz(CharacterId character) => character == Klutz;

    /// <inheritdoc />
    public bool IsEvilTwinPair(AbilityId ability) => ability == EvilTwinAbility.PairAbility;
}
