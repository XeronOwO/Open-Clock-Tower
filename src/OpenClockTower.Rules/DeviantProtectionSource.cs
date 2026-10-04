using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 怪咖「免死」的死亡保护来源（R-0048）：只覆盖流放致死；当天的裁定由日账读回。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《怪咖》· 2026-10-04 抓取 · 角色能力（「如果你表现得很有趣，当天你不能被流放」）与两则
/// 范例；《免死》分类页只描述保护的通用后果，**范围以角色自身文本为准**（R-0048 第 1 条）。
/// </para>
/// <para>
/// 只有"这个席位是怪咖 + 死因是流放"时才表态：不是怪咖 / 死因不是流放 → null（与本来源无关）；
/// 是怪咖但能力不生效（死亡 / 醉酒 / 中毒）→ 不受保护；能力生效且当天还没裁定 → 待说书人裁定；
/// 已裁定 → 按裁定的「有趣 / 无趣」返回。维度观测不齐 → 判定不了（D-0015：不猜）。
/// </para>
/// <para>
/// **纯函数**：只读账、当天账与座次，不写账、不产事件。
/// </para>
/// </remarks>
internal sealed class DeviantProtectionSource : IDeathProtectionSource
{
    private static readonly CharacterId Deviant = new("deviant");

    /// <inheritdoc />
    public CharacterId Character => Deviant;

    /// <inheritdoc />
    public DeathProtectionAssessment? Evaluate(DeathProtectionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Cause != DeathProtectionCause.Exile)
        {
            // 能力文本是「当天你不能被流放」：处决路径不属于它的保护范围（R-0048 第 1 条）。
            return null;
        }

        if (context.State.Seat(context.Seat) is not { } entry)
        {
            return null;
        }

        if (entry.CharacterValue is null)
        {
            return new DeathProtectionAssessment
            {
                Outcome = DeathProtectionOutcome.Indeterminate,
                Note = $"席位 {context.Seat.Value} 的角色还没有观测：无法判定是不是怪咖（不猜）",
            };
        }

        if (entry.CharacterValue != Deviant)
        {
            return null;
        }

        var effectiveness = AbilityEffectivenessEvaluator.Evaluate(context.State, entry);
        if (effectiveness is null)
        {
            return new DeathProtectionAssessment
            {
                Outcome = DeathProtectionOutcome.Indeterminate,
                Note = "怪咖的免死判定不了：席位的生死 / 醉酒 / 中毒还没有观测齐（不猜）",
            };
        }

        if (!effectiveness.Effective)
        {
            return new DeathProtectionAssessment
            {
                Outcome = DeathProtectionOutcome.NotProtected,
                Note = $"怪咖的免死不生效（{effectiveness.Note}）：本次流放照常死亡（R-0048）",
            };
        }

        if (context.Day?.ProtectionDecisionFor(context.Seat) is { } decision)
        {
            return decision.Protected
                ? new DeathProtectionAssessment
                {
                    Outcome = DeathProtectionOutcome.Protected,
                    Note = "说书人已裁定怪咖今天很有趣：本次流放达线但目标不死亡（R-0048）",
                }
                : new DeathProtectionAssessment
                {
                    Outcome = DeathProtectionOutcome.NotProtected,
                    Note = "说书人已裁定怪咖今天不够有趣：本次流放照常死亡（R-0048）",
                };
        }

        return new DeathProtectionAssessment
        {
            Outcome = DeathProtectionOutcome.NeedsRuling,
            Note = "怪咖今天是否有趣还没有裁定：先说书人裁定（有趣 → 今天不能被流放），再重新计票（R-0048）",
        };
    }
}
