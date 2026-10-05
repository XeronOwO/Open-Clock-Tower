using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 亡骨魔的「保留能力 + 邻近镇民中毒」常驻来源：按状态账里的击杀事实，算出此刻应当存在的那两条
/// 持续型效果——被杀死爪牙身上的「保留能力」窗口，与他**选定那一侧**最近的镇民身上的中毒。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-01 抓取）：《亡骨魔》· 角色能力——「被你杀死的爪牙保留他的能力，
/// 且与他邻近的两名镇民之一中毒」；· 规则细节 14 / 23——「距离爪牙顺时针或逆时针最近的镇民玩家中毒」
/// 「一旦标记了此『中毒』标记的玩家不再是……邻近的镇民玩家之一，就需要移动『中毒』标记至与
/// 『保留能力』的爪牙玩家**同一侧**的满足条件的玩家角色标记旁（不会因此替换到另一侧去）」；
/// · 20 / 24——标记的移除时机都是「亡骨魔死亡或离场，或被标记的玩家角色不再是爪牙角色」。
/// </para>
/// <para>
/// 这里只算**期望**（谁该带哪条效果），不产事件：补什么、终止什么、维度怎么解除由
/// <see cref="SettlementReconciler"/> 统一收口（D-0015）。目标随座次 / 角色变化**重算**：
/// 中毒效果的标识含目标席位，所以"换人"表现为旧标识消失、新标识出现（与诺-达鲺同款）。
/// </para>
/// <para>
/// **标记一经移除不复原**：「保留能力」与「中毒」两条标记同生同灭（规则细节 21 / 24），
/// 移除之后即使该席位又变回爪牙角色也不再回来——否则同一条事实会被反复兑现。
/// 判据是账上这条窗口是否已有**已终止**的实例（终止不可逆，R-0012）。
/// </para>
/// </remarks>
public sealed class VigormortisRetentionSource : IStandingEffectSource
{
    /// <inheritdoc />
    public AbilityId Ability => VigormortisAbility.RetentionAbility;

    /// <inheritdoc />
    public StandingEffectAssessment Evaluate(StandingEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        var seats = context.Seats;
        if (state.VigormortisKills.Count == 0)
        {
            // 还没有任何击杀事实：期望集为空，对账会把遗留效果终止掉。
            return StandingEffectAssessment.Conclusive([]);
        }

        if (seats.Count == 0)
        {
            return StandingEffectAssessment.Inconclusive("座次为空：算不出邻近镇民");
        }

        var characters = new CharacterId?[seats.Count];
        var types = new CharacterType[seats.Count];
        for (var index = 0; index < seats.Count; index++)
        {
            characters[index] = state.Seat(seats[index])?.CharacterValue;
            if (characters[index] is null)
            {
                return StandingEffectAssessment.Inconclusive(
                    $"席位 {seats[index].Value} 的角色尚未观测：算不出邻近镇民，本次不重算");
            }

            if (SectsAndVioletsRoster.TypeOf(characters[index]!.Value) is not { } type)
            {
                return StandingEffectAssessment.Inconclusive(
                    $"席位 {seats[index].Value} 的角色不在首版花名册里：算不出镇民集合，本次不重算");
            }

            types[index] = type;
        }

        var expectations = new List<StandingEffectExpectation>(state.VigormortisKills.Count * 2);
        foreach (var kill in state.VigormortisKills)
        {
            var demonIndex = IndexOf(seats, kill.Demon);
            if (demonIndex < 0)
            {
                // 亡骨魔离场：标记移除（规则细节 20 / 24）。
                continue;
            }

            if (characters[demonIndex] != VigormortisAbility.Character)
            {
                // 这个席位已经不是亡骨魔：能力不再存在，标记移除。
                continue;
            }

            switch (state.AbilityPresentOn(kill.Demon))
            {
                case true:
                    break;
                case null:
                    return StandingEffectAssessment.Inconclusive(
                        $"亡骨魔（{kill.Demon.Value} 号）是否握有能力判定不了：本次不重算");
                default:
                    // 死亡（且没有重获）/ 失去能力：中毒者恢复健康、保留能力结束。
                    continue;
            }

            var minionIndex = IndexOf(seats, kill.Minion);
            if (minionIndex < 0)
            {
                continue;
            }

            if (state.Seat(kill.Minion)?.LifeValue is not { } minionLife)
            {
                return StandingEffectAssessment.Inconclusive(
                    $"席位 {kill.Minion.Value} 的生死尚未观测：本次不重算");
            }

            if (minionLife != LifeState.Dead)
            {
                // 死亡还没落地（待定死亡尚未裁定 / 被说书人阻止）：击杀事实此时不生效。
                continue;
            }

            if (!KilledByVigormortis(state, kill))
            {
                // 记录在、击杀却没落地（例如麻脸巫婆之夜的待定死亡被阻止后他又因别的原因死亡）：
                // 「被亡骨魔**成功**杀死」不成立，标记不放置（规则细节 19）。
                continue;
            }

            if (types[minionIndex] != CharacterType.Minion)
            {
                // 被标记的玩家角色不再是爪牙角色：两条标记一并移除（规则细节 8 / 20 / 24）。
                continue;
            }

            var retainId = VigormortisAbility.RetainEffectId(kill.Demon, kill.Minion);
            if (state.PersistentEffects.Any(effect => effect.Id == retainId && effect.IsTerminated))
            {
                // 标记移除过就不再回来（终止不可逆）：局内他不可能被亡骨魔再杀一次，
                // 因此这条事实已经兑现完毕。
                continue;
            }

            expectations.Add(new StandingEffectExpectation
            {
                Id = retainId,
                Ability = VigormortisAbility.RetentionAbility,
                Source = kill.Demon,
                SourceCharacter = VigormortisAbility.Character,
                Target = kill.Minion,
                Window = EffectWindowKind.RetainedAbility,
            });

            if (kill.Side is { } side && NearestTownsfolk(types, minionIndex, side) is { } poisoned)
            {
                expectations.Add(new StandingEffectExpectation
                {
                    Id = VigormortisAbility.PoisonEffectId(kill.Demon, kill.Minion, seats[poisoned]),
                    Ability = VigormortisAbility.RetentionAbility,
                    Source = kill.Demon,
                    SourceCharacter = VigormortisAbility.Character,
                    Target = seats[poisoned],
                    Dimension = EffectDimension.Poison,
                });
            }
        }

        return StandingEffectAssessment.Conclusive(expectations);
    }

    /// <summary>这名爪牙确实是**被这位亡骨魔成功杀死**的：击杀事实在账上有配套的即时型效果。</summary>
    /// <remarks>
    /// 记录只说明"说书人作出了选择"，击杀效果才是"成功杀死"的落地证据（规则细节 19 的措辞是
    /// 「被亡骨魔**成功**杀死」）。两者都在，标记才放置。
    /// </remarks>
    private static bool KilledByVigormortis(GameState state, VigormortisKill kill) =>
        state.InstantaneousEffects.Any(effect =>
            effect.Source == kill.Demon
            && effect.Ability == VigormortisAbility.KillAbility
            && effect.Target == kill.Minion);

    /// <summary>席位在环上的下标；不在环上（离场）返回 -1。</summary>
    private static int IndexOf(IReadOnlyList<SeatId> seats, SeatId seat)
    {
        for (var index = 0; index < seats.Count; index++)
        {
            if (seats[index] == seat)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// 从爪牙席位沿选定方向数，最近的镇民（跳过非镇民角色）；找不到返回 null——
    /// 与诺-达鲺同款：只有全场一个镇民时两个方向会数到同一人，说书人的"选侧"没有实际区别。
    /// </summary>
    private static int? NearestTownsfolk(CharacterType[] types, int from, SeatRingDirection side)
    {
        var step = side == SeatRingDirection.Clockwise ? 1 : -1;
        for (var offset = 1; offset < types.Length; offset++)
        {
            var index = ((from + (step * offset)) % types.Length + types.Length) % types.Length;
            if (types[index] == CharacterType.Townsfolk)
            {
                return index;
            }
        }

        return null;
    }
}
