namespace OpenClockTower.Kernel;

/// <summary>
/// 死亡保护查询的聚合：把 <see cref="SettlementContext.DeathProtections"/> 里各来源的判定收成一个结论。
/// </summary>
/// <remarks>
/// <para>
/// 优先级（R-0048）：受保护 &gt; 判定不了 &gt; 待裁定 &gt; 不受保护。受保护短路——只要有一个来源说
/// 受保护，死亡就不产生；判定不了优先于待裁定——裁定救不回缺观测的判定，先补观测。
/// </para>
/// <para>
/// 没有来源（内核夹具 / 只推进不结算）→ 不受保护：行为与保护机制引入前一致。
/// </para>
/// </remarks>
internal static class DeathProtectionQuery
{
    /// <summary>按死因聚合所有来源；返回的说明来自优先级最高的那一档（没有来源时为默认说明）。</summary>
    internal static DeathProtectionAssessment Resolve(
        SettlementContext context,
        DayRecord? day,
        SeatId seat,
        DeathProtectionCause cause)
    {
        ArgumentNullException.ThrowIfNull(context);

        var assessments = new List<DeathProtectionAssessment>(context.DeathProtections.Count);
        foreach (var source in context.DeathProtections)
        {
            var assessment = source.Evaluate(new DeathProtectionContext
            {
                State = context.State,
                Seats = context.Seats,
                Seat = seat,
                Cause = cause,
                Day = day,
            });

            // null = 与本来源无关：不参与聚合（不是"不受保护"的票）。
            if (assessment is not null)
            {
                assessments.Add(assessment);
            }
        }

        return assessments.Count == 0
            ? Default(context.DeathProtections.Count > 0)
            : Pick(assessments);
    }

    /// <summary>没有来源"表态"时的默认结论：登记了来源但都没覆盖这次死亡，或本局根本没有来源。</summary>
    private static DeathProtectionAssessment Default(bool anySources) => new()
    {
        Outcome = DeathProtectionOutcome.NotProtected,
        Note = anySources
            ? "没有死亡保护来源覆盖这次死亡：不受保护"
            : "本局没有登记死亡保护来源：这次死亡不被保护",
    };

    /// <summary>按「受保护 &gt; 判定不了 &gt; 待裁定 &gt; 不受保护」取第一条。</summary>
    private static DeathProtectionAssessment Pick(IReadOnlyList<DeathProtectionAssessment> assessments)
    {
        foreach (var outcome in new[]
        {
            DeathProtectionOutcome.Protected,
            DeathProtectionOutcome.Indeterminate,
            DeathProtectionOutcome.NeedsRuling,
        })
        {
            foreach (var assessment in assessments)
            {
                if (assessment.Outcome == outcome)
                {
                    return assessment;
                }
            }
        }

        // 全部来源都给出「不受保护」时，用第一条的说明（来源各自说明了自己为什么不管）。
        return assessments[0];
    }
}
