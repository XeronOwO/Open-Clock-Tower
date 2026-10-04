namespace OpenClockTower.Kernel;

/// <summary>
/// 屠夫「额外提名窗口」的纯迁移（百科《屠夫》；R-0050）：首次处决后的开窗判定与窗口内额外提名的受理。
/// </summary>
/// <remarks>
/// <para>
/// **开窗**：当天首次处决事实产出后调用——有可用屠夫 → 窗口打开事件（白天保持 Open）；没有 → 空结果，
/// 由 <see cref="DayMachine.CloseDay"/> 照常关账；判定不了 → 显式拒绝（不猜）。
/// </para>
/// <para>
/// **额外提名**：只受理「窗口开着 + 发起人 = 窗口授予席位」；《屠夫》明文豁免「每人每天一次」
/// 「每人每天被提名一次」两条当日限制，其余（同一时间至多一项提名、在局、存活）照旧（R-0050 第 3 条）。
/// </para>
/// <para>**纯计算**（D-0008）：事实从账读，不猜；有没有屠夫由规则层来源回答（R-0050）。</para>
/// </remarks>
internal static class ExtraNominationMachine
{
    /// <summary>首次处决后判定开窗；产出窗口打开事件，空事件表示没有可用屠夫。</summary>
    internal static DayOutcome OpenAfterFirstExecution(
        DayRecord day,
        SettlementContext context,
        SeatId executedSeat)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(context);

        var assessment = ExtraNominationQuery.Resolve(context, day, executedSeat);
        return assessment.Outcome switch
        {
            ExtraNominationOutcome.Available when assessment.Seat is { } seat => DayOutcome.Accepted(
            [
                new ExtraNominationWindowOpenedEvent
                {
                    DayNumber = day.DayNumber,
                    Seat = seat,
                },
            ]),
            ExtraNominationOutcome.Available => throw new InvalidOperationException(
                "额外提名来源给出「可用」结论但没有给出窗口授予席位：来源契约被破坏"),
            ExtraNominationOutcome.Indeterminate => DayOutcome.Reject(
                "day.extra_nomination_indeterminate",
                $"{assessment.Note}（先补观测，再结束白天；R-0050）"),
            _ => DayOutcome.Accepted([]),
        };
    }

    /// <summary>受理一条额外提名输入；不满足条件时显式拒绝（不产出任何事件）。</summary>
    internal static DayOutcome Nominate(DayState state, SettlementContext context, NominateExtraInput input)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);

        if (state.OpenDay is not { } day)
        {
            return DayOutcome.Reject("day.not_open", "现在不是白天，或白天已经结束");
        }

        if (day.OpenNomination is not null)
        {
            return DayOutcome.Reject(
                "day.nomination_in_progress",
                "已有一项提名在投票中：先计票（CountVotes）或强推兜底，不能同时提两名玩家");
        }

        if (day.ExtraNomination is not { Status: ExtraNominationWindowStatus.Open } window)
        {
            return DayOutcome.Reject(
                "day.extra_nomination_window_not_open",
                "现在没有额外提名窗口：只有当天首次处决后、且窗口还没被用掉时才能额外提名（R-0050）");
        }

        if (input.Nominator != window.Seat)
        {
            return DayOutcome.Reject(
                "day.extra_nomination_not_granted",
                $"席位 {input.Nominator.Value} 不是窗口授予席位 {window.Seat.Value}："
                    + "额外提名只能由屠夫本人发起（R-0050）");
        }

        if (!context.Seats.Contains(input.Nominator))
        {
            return DayOutcome.Reject("day.nominator_unknown", $"席位 {input.Nominator.Value} 不在本局座次里");
        }

        if (!context.Seats.Contains(input.Nominee))
        {
            return DayOutcome.Reject("day.nominee_unknown", $"席位 {input.Nominee.Value} 不在本局座次里");
        }

        var nominatorLife = context.State.Seat(input.Nominator)?.LifeValue;
        if (nominatorLife is null)
        {
            return DayOutcome.Reject(
                "day.nominator_life_unknown",
                $"席位 {input.Nominator.Value} 的生死还没有观测：无法判定他能不能发起提名（不猜）");
        }

        if (nominatorLife != LifeState.Alive)
        {
            // 集骨者的「重获能力」让死者带着角色能力行动（R-0054）：屠夫在重获窗口内照样能发起
            // 额外提名（百科《集骨者》范例：重获后的屠夫在次日处决后获得额外提名）。
            if (context.State.AbilityPresentOn(input.Nominator) != true)
            {
                return DayOutcome.Reject(
                    "day.nominator_dead",
                    $"席位 {input.Nominator.Value} 已经死亡：只有存活玩家可以发起提名（《屠夫》窗口同理）");
            }
        }

        // 提名者此刻的角色快照随事件落账（与常规提名同一口径；城镇公告员按 R-0037 读它）。
        var nominatorCharacter = context.State.Seat(input.Nominator)?.CharacterValue;

        return DayOutcome.Accepted(
        [
            new ExtraNominationMadeEvent
            {
                DayNumber = day.DayNumber,
                NominationIndex = day.Nominations.Count + 1,
                Nominator = input.Nominator,
                Nominee = input.Nominee,
                NominatorCharacter = nominatorCharacter,
            },
        ]);
    }
}
