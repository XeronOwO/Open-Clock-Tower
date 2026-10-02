using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 哲学家的常驻醉酒：他获得的能力所属角色**在场**时，该角色的持有者醉酒（动态检测）。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《哲学家》· 2026-10-01 抓取 · 运作方式 4——「如果哲学家选择了一个已经在游戏中的角色，
/// 该角色对应的玩家醉酒」；运作方式 6——「如果哲学家之前选择了不在场的角色，但之后被选择的角色在场了，
/// 被选择的角色对应的玩家也会醉酒」；规则细节 1——同一角色在场超过一人时只让其中一名醉酒，
/// 通常应优先让**存活**玩家醉酒；运作方式 5 / 14——哲学家死亡 / 醉酒 / 中毒时那名玩家恢复清醒。
/// </para>
/// <para>
/// 这里只算**期望**（谁该醉酒），施加 / 终止 / 维度解除由 <see cref="SettlementReconciler"/> 统一收口
/// （与诺-达鲺的中毒同族）：目标随持有者变化而**移动** = 旧效果终止 + 新效果施加。
/// 哲学家醉酒 / 中毒时这条效果由来源状态**挂起**（R-0012），恢复后继续；他死亡或换角色时
/// 「获得能力」事实终止，这条醉酒随之终止（那名玩家永久恢复清醒）。
/// </para>
/// </remarks>
public sealed class PhilosopherDrunkSource : IStandingEffectSource
{
    /// <inheritdoc />
    public AbilityId Ability => PhilosopherAbility.DrunkAbility;

    /// <inheritdoc />
    public StandingEffectAssessment Evaluate(StandingEffectContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (PhilosopherAbility.FindGrant(context.State) is not { } grant)
        {
            // 还没获得能力（或事实已终止）：期望集为空，对账会把遗留的醉酒终止掉。
            return StandingEffectAssessment.Conclusive([]);
        }

        if (grant.GrantedCharacter is not { } granted)
        {
            return StandingEffectAssessment.Inconclusive(
                "「获得能力」事实没有记下被获得的角色：算不出该让谁醉酒，本次不重算");
        }

        var holders = PhilosopherAbility.HoldersOf(context.State, granted);
        if (holders.Count == 0)
        {
            // 被选角色还不在场：现在没有可醉的人；它后来进场时按动态检测再落（运作方式 6）。
            return StandingEffectAssessment.Conclusive([]);
        }

        var living = holders.Where(holder => holder.LifeValue == LifeState.Alive).ToArray();
        SeatStateEntry target;
        if (living.Length == 1)
        {
            target = living[0];
        }
        else if (living.Length == 0 && holders.Count == 1)
        {
            // 唯一持有者已死亡：醉酒标记仍跟着它（「该角色对应的玩家」就是它），
            // 不转移到别人身上——不猜。
            target = holders[0];
        }
        else
        {
            return StandingEffectAssessment.Inconclusive(
                living.Length > 1
                    ? $"被获得的角色 {granted.Value} 有多名存活持有者（角色唯一本不该发生）：本次不重算"
                    : $"被获得的角色 {granted.Value} 有多名已死亡持有者且无人存活：本次不重算");
        }

        if (target.LifeValue is null)
        {
            return StandingEffectAssessment.Inconclusive(
                $"席位 {target.Seat.Value} 的生死尚未观测：本次不重算");
        }

        return StandingEffectAssessment.Conclusive(
        [
            new StandingEffectExpectation
            {
                Id = new EffectId(
                    $"standing:{PhilosopherAbility.DrunkAbility.Value}:{grant.Source.Value}:{target.Seat.Value}"),
                Ability = PhilosopherAbility.DrunkAbility,
                Source = grant.Source,
                SourceCharacter = PhilosopherAbility.Character,
                Target = target.Seat,
                Dimension = EffectDimension.Drunk,
            },
        ]);
    }
}
