namespace OpenClockTower.Kernel;

/// <summary>
/// 死亡保护裁定的受理条件评估（R-0048）：说书人裁定机器与说书人投影提示共用同一判定。
/// </summary>
/// <remarks>
/// <para>
/// 条件链（顺序即语义，与裁定机器引入时一致）：调用方保证白天开着 → 席位在局 → 该席位今天未裁定 →
/// 生死已观测且存活 → 有未结清的流放且目标是它 → 收票收完 → 票面达线 → 死亡保护查询出结论。
/// </para>
/// <para>
/// **纯评估**（D-0008 / D-0015）：只读账与上下文，不产事件、不写账、不猜；拒绝码与文案由裁定机器
/// 原样回给调用方，投影侧只在 <see cref="Kind.NeedsRuling"/> / <see cref="Kind.Indeterminate"/> 时给提示。
/// </para>
/// </remarks>
internal static class DayProtectionEligibility
{
    /// <summary>评估结论。</summary>
    internal sealed record Result
    {
        /// <summary>结论分类。</summary>
        internal required Kind Kind { get; init; }

        /// <summary>不受理 / 判定不了时的拒绝码（<see cref="Kind.NeedsRuling"/> 时为空串）。</summary>
        internal required string Code { get; init; }

        /// <summary>给说书人的说明（差什么 / 下一步做什么）。</summary>
        internal required string Message { get; init; }
    }

    /// <summary>评估结论分类。</summary>
    internal enum Kind
    {
        /// <summary>当前不该受理（没有待裁定 / 已经裁定 / 没达线 / 没目标 / 目标已死等）。</summary>
        NotRequired,

        /// <summary>满足全部受理条件，且保护来源要求说书人裁定。</summary>
        NeedsRuling,

        /// <summary>满足受理条件但维度观测不齐：先补观测，再裁定（不猜）。</summary>
        Indeterminate,
    }

    /// <summary>评估「此刻这一席的死亡保护裁定」是否受理；<paramref name="day"/> 必须是进行中的白天。</summary>
    internal static Result Assess(SettlementContext context, DayRecord day, SeatId seat)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(day);

        if (!context.Seats.Contains(seat))
        {
            return Reject(
                "day.protection_seat_unknown",
                $"席位 {seat.Value} 不在本局在局座次里：不能给它裁定死亡保护");
        }

        if (day.ProtectionDecisionFor(seat) is not null)
        {
            return Reject(
                "day.protection_already_decided",
                $"席位 {seat.Value} 今天已经裁定过死亡保护：每席位每天至多一条（R-0048）");
        }

        // 达线时裁定的另一半前提是「目标存活」：已死亡的目标不会再死亡，裁定没有意义；
        // 生死没观测齐时不猜（D-0015）——先补观测，再裁定 / 计票。
        var life = context.State.Seat(seat)?.LifeValue;
        if (life is null)
        {
            return Reject(
                "day.protection_life_unknown",
                $"席位 {seat.Value} 的生死还没有观测：无法受理死亡保护裁定（不猜）");
        }

        if (life != LifeState.Alive)
        {
            return Reject(
                "day.protection_not_required",
                $"席位 {seat.Value} 已经死亡：死亡保护不会改变结果，不需要裁定（R-0048）");
        }

        if (day.OpenExile is not { } exile || exile.Target != seat)
        {
            return Reject(
                "day.protection_not_required",
                $"今天没有针对席位 {seat.Value} 的未结清流放：死亡保护裁定只在流放达线时受理（R-0048）");
        }

        if (exile.Sweep is not { IsComplete: true } sweep)
        {
            return Reject(
                "day.protection_not_required",
                $"第 {exile.Index} 条流放的收票还没有收完：达线之后才需要裁定死亡保护（R-0048）");
        }

        if (exile.Ballot.Count * 2 < sweep.Seats.Count)
        {
            return Reject(
                "day.protection_not_required",
                $"第 {exile.Index} 条流放没有达线（{exile.Ballot.Count} 票 / {sweep.Seats.Count} 席）："
                    + "不需要裁定死亡保护（R-0048）");
        }

        var assessment = DeathProtectionQuery.Resolve(context, day, seat, DeathProtectionCause.Exile);
        return assessment.Outcome switch
        {
            DeathProtectionOutcome.NeedsRuling => new Result
            {
                Kind = Kind.NeedsRuling,
                Code = string.Empty,
                Message = assessment.Note,
            },
            DeathProtectionOutcome.Indeterminate => new Result
            {
                Kind = Kind.Indeterminate,
                Code = "day.protection_indeterminate",
                Message = $"{assessment.Note}（判定不了时先补观测，再裁定；R-0048）",
            },
            _ => new Result
            {
                Kind = Kind.NotRequired,
                Code = "day.protection_not_required",
                Message = assessment.Note,
            },
        };
    }

    private static Result Reject(string code, string message) => new()
    {
        Kind = Kind.NotRequired,
        Code = code,
        Message = message,
    };
}
