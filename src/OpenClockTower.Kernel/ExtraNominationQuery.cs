namespace OpenClockTower.Kernel;

/// <summary>
/// 额外提名窗口查询的聚合：把 <see cref="SettlementContext.ExtraNominations"/> 里各来源的判定收成一个结论
/// （R-0050）。
/// </summary>
/// <remarks>
/// <para>
/// 优先级：可用 &gt; 判定不了 &gt; 不可用。可用短路——按登记顺序取第一个给出授予席位的来源，结果稳定；
/// 判定不了优先于不可用——观测不齐时先补观测，不允许默认"没有屠夫"静默关账。
/// </para>
/// <para>
/// 没有来源（内核夹具 / 只推进不结算）→ 不可用：行为与 D4 引入前一致。
/// </para>
/// </remarks>
internal static class ExtraNominationQuery
{
    /// <summary>聚合所有来源；返回结论与说明（没有来源时为默认「不可用」）。</summary>
    internal static ExtraNominationAssessment Resolve(
        SettlementContext context,
        DayRecord? day,
        SeatId executedSeat)
    {
        ArgumentNullException.ThrowIfNull(context);

        var assessments = new List<ExtraNominationAssessment>(context.ExtraNominations.Count);
        foreach (var source in context.ExtraNominations)
        {
            var assessment = source.Evaluate(new ExtraNominationContext
            {
                State = context.State,
                Seats = context.Seats,
                Day = day,
                ExecutedSeat = executedSeat,
            });

            // null = 与本来源无关：不参与聚合（不是"不可用"的票）。
            if (assessment is not null)
            {
                assessments.Add(assessment);
            }
        }

        if (assessments.Count == 0)
        {
            return new ExtraNominationAssessment
            {
                Outcome = ExtraNominationOutcome.Unavailable,
                Note = context.ExtraNominations.Count > 0
                    ? "没有额外提名来源覆盖这次处决：照常关闭白天"
                    : "本局没有登记额外提名来源：照常关闭白天",
            };
        }

        foreach (var outcome in new[]
        {
            ExtraNominationOutcome.Available,
            ExtraNominationOutcome.Indeterminate,
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

        return assessments[0];
    }
}
