using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 洗脑师处罚处决的依据：目标身上有一条未撤下、且来源仍生效的疯狂要求。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《洗脑师》· 2026-10-01 抓取 · 运作方式——「在下一个白天或夜晚，如果被选择的玩家没有尽
/// 最大努力去说服其他玩家他是被选择的角色，你就可以处决他」；· 角色简介 7——「执行处决惩罚计入每天的
/// 处决限制」。口径见 <c>docs/standard/rulings.md</c> R-0020 / R-0021。
/// </para>
/// <para>
/// 来源醉酒 / 中毒时要求**保留但不生效**（R-0012 的挂起口径）：因此这里拒绝处罚，而不是撤下要求；
/// 多条要求同时挂在一席（跨夜转移的过渡，R-0021 第 2 条）时，只要有一条来源生效即可处罚。
/// </para>
/// </remarks>
internal sealed class CerenovusMadnessPunishment : IAdjudicatedExecutionSource
{
    /// <inheritdoc />
    public MadnessPunishmentSource Source => MadnessPunishmentSource.Cerenovus;

    /// <inheritdoc />
    public AdjudicatedExecutionEligibility Evaluate(GameState state, IReadOnlyList<SeatId> seats, SeatId seat)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);

        var requirements = state.LiveRequirementsOn(seat)
            .Where(requirement => requirement.Ability == CerenovusAbility.MadnessAbility)
            .ToArray();

        if (requirements.Length == 0)
        {
            return new AdjudicatedExecutionEligibility
            {
                Applicable = false,
                Note = $"席位 {seat.Value} 身上没有未撤下的洗脑师要求：不能以「洗脑师的疯狂处罚」处决（R-0021）",
            };
        }

        var indeterminate = false;
        foreach (var requirement in requirements)
        {
            switch (state.IsOperative(requirement))
            {
                case true:
                    return new AdjudicatedExecutionEligibility
                    {
                        Applicable = true,
                        Note = $"席位 {seat.Value} 的疯狂要求仍在生效：要证明自己是「{requirement.ProveToBe}」",
                        DeathReason = $"{CerenovusAbility.PunishmentDeathReason}："
                            + $"未尽力让其他人相信自己是「{requirement.ProveToBe}」",
                        CausedBy = requirement.Source,
                        EffectId = new EffectId(requirement.Id.Value),
                    };
                case null:
                    indeterminate = true;
                    break;
                default:
                    break;
            }
        }

        return indeterminate
            ? new AdjudicatedExecutionEligibility
            {
                Applicable = null,
                Note = $"来源席位 {requirements[0].Source.Value} 的生死 / 醉酒 / 中毒还没有观测齐："
                    + "无法判定要求是否仍生效（不猜，D-0015）",
            }
            : new AdjudicatedExecutionEligibility
            {
                Applicable = false,
                Note = "洗脑师的要求还在账上，但来源此刻不生效（死亡 / 醉酒 / 中毒，R-0004 / R-0012）："
                    + "不能处罚处决",
            };
    }
}
