using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 诺-达鲺的常驻中毒：与他顺时针 / 逆时针方向上最近的两名镇民中毒（无论存活还是死亡）。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《诺-达鲺》· 2026-10-01 抓取 · 角色简介 / 运作方式 / 提示标记：
/// 「在诺-达鲺顺时针和逆时针方向上最近的镇民中毒，无论这些镇民是存活还是死亡。
/// 如果诺-达鲺死亡或者因为其他原因失去能力，那么这两名镇民玩家恢复健康。总是会有两名镇民玩家因此中毒，
/// 诺-达鲺的效果会跳过与他相邻的非镇民角色」；「一旦诺-达鲺及其邻近的玩家角色的座次或角色发生变化，
/// 就需要移动『中毒』标记至满足条件的玩家角色标记旁」。
/// </para>
/// <para>
/// 这里只算**期望**（谁该中毒），不产事件：终止 / 施加 / 维度解除由
/// <see cref="SettlementReconciler"/> 统一收口，保证「效果 → 维度」只有一个写入方（D-0015）。
/// 座次取服务端席位名单的自然顺序（座位号升序 = 圆桌顺序）。
/// </para>
/// </remarks>
public sealed class NoDashiiPoisonSource : IStandingEffectSource
{
    /// <summary>常驻中毒的能力标识：与夜间击杀分开记（同角色两个能力）。</summary>
    public static readonly AbilityId PoisonAbility = new("no-dashii.poison");

    private static readonly CharacterId NoDashii = new("no-dashii");

    /// <inheritdoc />
    public AbilityId Ability => PoisonAbility;

    /// <inheritdoc />
    public StandingEffectAssessment Evaluate(StandingEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seats = context.Seats;
        if (seats.Count == 0)
        {
            return StandingEffectAssessment.Inconclusive("座次为空：算不出邻近镇民");
        }

        var characters = new CharacterId?[seats.Count];
        for (var index = 0; index < seats.Count; index++)
        {
            characters[index] = context.State.Seat(seats[index])?.CharacterValue;
            if (characters[index] is null)
            {
                return StandingEffectAssessment.Inconclusive(
                    $"席位 {seats[index].Value} 的角色尚未观测：算不出邻近镇民，本次不重算");
            }
        }

        var demonIndex = -1;
        for (var index = 0; index < seats.Count; index++)
        {
            if (characters[index] == NoDashii)
            {
                demonIndex = index;
                break;
            }
        }

        if (demonIndex < 0)
        {
            // 诺-达鲺不在场：期望集为空，对账会把遗留的中毒效果终止掉。
            return StandingEffectAssessment.Conclusive([]);
        }

        if (context.State.Seat(seats[demonIndex])?.LifeValue is not { } demonLife)
        {
            return StandingEffectAssessment.Inconclusive(
                $"诺-达鲺（{seats[demonIndex].Value} 号）的生死尚未观测：本次不重算");
        }

        if (demonLife == LifeState.Dead)
        {
            switch (context.State.RegainedAbilityOn(seats[demonIndex]))
            {
                case true:
                    // 集骨者「重获能力」：死者重新握有能力，继续按在场下毒（R-0054）。
                    break;
                case null:
                    return StandingEffectAssessment.Inconclusive(
                        $"诺-达鲺（{seats[demonIndex].Value} 号）的重获能力窗口判定不了：本次不重算");
                default:
                    // 玩家死亡即失去角色能力（百科《术语汇总》「死亡」）；既有中毒效果由折叠终止、
                    // 维度由重算解除，对账本身不再期望任何新效果——别让死人继续下毒。
                    return StandingEffectAssessment.Conclusive([]);
            }
        }

        var types = new CharacterType[seats.Count];
        for (var index = 0; index < seats.Count; index++)
        {
            if (SectsAndVioletsRoster.TypeOf(characters[index]!.Value) is not { } type)
            {
                return StandingEffectAssessment.Inconclusive(
                    $"席位 {seats[index].Value} 的角色不在首版花名册里：算不出镇民集合，本次不重算");
            }

            types[index] = type;
        }

        var clockwise = NearestTownsfolk(types, demonIndex, step: 1);
        var counterClockwise = NearestTownsfolk(types, demonIndex, step: -1);
        if (clockwise < 0 || counterClockwise < 0)
        {
            return StandingEffectAssessment.Inconclusive(
                "场上找不到足够的镇民：按规则总有两名，数据或实现有缺陷，本次不重算");
        }

        var expectations = new List<StandingEffectExpectation>(capacity: 2);
        AddExpectation(expectations, seats[demonIndex], seats[clockwise]);
        AddExpectation(expectations, seats[demonIndex], seats[counterClockwise]);
        return StandingEffectAssessment.Conclusive(expectations);
    }

    private static int NearestTownsfolk(CharacterType[] types, int from, int step)
    {
        for (var offset = 1; offset < types.Length; offset++)
        {
            var index = ((from + (step * offset)) % types.Length + types.Length) % types.Length;
            if (types[index] == CharacterType.Townsfolk)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>同一个目标（场上只有一名镇民时两侧会取到同一人）只留一条期望，标识保持稳定。</summary>
    private static void AddExpectation(
        List<StandingEffectExpectation> expectations,
        SeatId source,
        SeatId target)
    {
        if (expectations.Any(expectation => expectation.Target == target))
        {
            return;
        }

        expectations.Add(new StandingEffectExpectation
        {
            Id = new EffectId($"standing:{PoisonAbility.Value}:{source.Value}:{target.Value}"),
            Ability = PoisonAbility,
            Source = source,
            SourceCharacter = NoDashii,
            Target = target,
            Dimension = EffectDimension.Poison,
        });
    }
}
