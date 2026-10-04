using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 屠夫「每个白天，首次处决后，你可以再次发起提名」的开窗来源（R-0050）：判定本局有没有可用屠夫。
/// </summary>
/// <remarks>
/// <para>
/// 依据百科《屠夫》· 2026-10-04 抓取 · 角色能力 / 角色简介 / 运作方式：只要当天已经发生首次处决
/// （调用时机由内核保证），屠夫本人存活且能力生效（不醉酒不中毒）就可用。
/// </para>
/// <para>
/// 只有"本局有屠夫"时才表态：没有屠夫 → null（与本来源无关，照常关账）；屠夫已死亡 / 醉酒 / 中毒 →
/// 不可用；生死 / 醉酒 / 中毒观测不齐 → 判定不了；出现多个屠夫席位 → 判定不了（花名册不出重复角色，
/// 出现即数据异常，按 D-0015 不猜）。有在局席位角色未观测时同样不猜——无法排除其中藏着屠夫。
/// </para>
/// <para>**纯函数**：只读账、座次与当天账，不写账、不产事件。</para>
/// </remarks>
internal sealed class ButcherExtraNominationSource : IExtraNominationSource
{
    private static readonly CharacterId Butcher = new("butcher");

    /// <inheritdoc />
    public CharacterId Character => Butcher;

    /// <inheritdoc />
    public ExtraNominationAssessment? Evaluate(ExtraNominationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var butchers = new List<SeatStateEntry>();
        var unobserved = new List<SeatId>();
        foreach (var seat in context.Seats)
        {
            var entry = context.State.Seat(seat);
            if (entry?.CharacterValue is not { } character)
            {
                unobserved.Add(seat);
                continue;
            }

            if (character == Butcher)
            {
                butchers.Add(entry);
            }
        }

        if (butchers.Count > 1)
        {
            return new ExtraNominationAssessment
            {
                Outcome = ExtraNominationOutcome.Indeterminate,
                Note = $"本局在局座次里出现 {butchers.Count} 个屠夫席位：无法判定该由谁开窗（不猜；R-0050）",
            };
        }

        if (butchers.Count == 0)
        {
            return unobserved.Count == 0
                ? null
                : new ExtraNominationAssessment
                {
                    Outcome = ExtraNominationOutcome.Indeterminate,
                    Note = $"有 {unobserved.Count} 个在局席位的角色还没有观测：无法排除其中藏着屠夫（不猜；R-0050）",
                };
        }

        var butcher = butchers[0];
        var effectiveness = AbilityEffectivenessEvaluator.Evaluate(butcher);
        if (effectiveness is null)
        {
            return new ExtraNominationAssessment
            {
                Outcome = ExtraNominationOutcome.Indeterminate,
                Note = $"屠夫（{butcher.Seat.Value} 号）的开窗判定不了：生死 / 醉酒 / 中毒还没有观测齐（不猜；R-0050）",
            };
        }

        if (!effectiveness.Effective)
        {
            return new ExtraNominationAssessment
            {
                Outcome = ExtraNominationOutcome.Unavailable,
                Note = $"屠夫的开窗不生效（{effectiveness.Note}）：照常关闭白天（R-0050）",
            };
        }

        return new ExtraNominationAssessment
        {
            Outcome = ExtraNominationOutcome.Available,
            Seat = butcher.Seat,
            Note = $"屠夫（{butcher.Seat.Value} 号）可用：首次处决后打开额外提名窗口（R-0050）",
        };
    }
}
