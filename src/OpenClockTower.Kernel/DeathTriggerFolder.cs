namespace OpenClockTower.Kernel;

/// <summary>
/// 死亡触发族（理发师 / 贤者 / 心上人 / 呆瓜）的事实账与幂等账的折叠。
/// </summary>
/// <remarks>
/// 从 <see cref="StepMachineFolder"/> 拆出（单文件 600 行门禁）：那边负责通用事件分发与阶段折叠，
/// 这里只负责死亡触发族的账——事实开启 / 关闭、跳过记录、跨阶段携带守卫。
/// 记账口径见 <c>docs/standard/rulings.md</c> R-0027 / R-0033 / R-0038 / R-0039。
/// </remarks>
internal static class DeathTriggerFolder
{
    /// <summary>
    /// 「今晚理发」事实跨阶段保留的守卫：只允许白天 → 夜晚（白天死亡的事件在**当夜**交互，
    /// 百科《死亡触发能力》· 2026-10-01 抓取 · 能力简介）；从夜晚带进新阶段说明夜末的
    /// 「过时不候」收口缺失，显式失败而不是静默顺延（D-0014 能力 3）。
    /// </summary>
    internal static BarberNight? CarryBarberNight(StepMachineState? state)
    {
        if (state?.BarberNight is not { } night)
        {
            return null;
        }

        if (state.Plan.Phase is GamePhase.FirstNight or GamePhase.OtherNight)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：夜晚（{state.Plan.Label}）已经结束，理发师之夜事实还没有收口"
                + "（过时不候的关闭事件缺失），不能把它顺延到新阶段");
        }

        return night;
    }

    /// <summary>
    /// 贤者事实的跨阶段守卫：它只属于当夜——死亡在恶魔段、展示在当夜贤者格；阶段边界上仍挂着
    /// 说明夜末的「过时不候」收口缺失，显式失败而不是静默顺延（R-0038）。
    /// </summary>
    internal static SageNight? CarrySageNight(StepMachineState? state)
    {
        if (state?.SageNight is not { } night)
        {
            return null;
        }

        throw new InvalidOperationException(
            $"事件流顺序损坏：上一阶段已经结束，贤者（{night.Sage.Value} 号）的展示事实还没有收口"
            + "（过时不候的关闭事件缺失），不能把它顺延到新阶段");
    }

    /// <summary>开启「今晚理发」事实；同一夜不能开两次（重复即事件流损坏）。</summary>
    internal static StepMachineState ApplyBarberNightOpened(StepMachineState? state, BarberNightOpenedEvent opened)
    {
        var current = StepMachineFolder.Require(state, opened);
        if (current.BarberNight is not null)
        {
            throw new InvalidOperationException("事件流顺序损坏：理发师之夜事实已经开启过，不能重复开启");
        }

        return current with
        {
            BarberNight = new BarberNight { Source = opened.Source, Note = opened.Note },
        };
    }

    /// <summary>关闭「今晚理发」事实：没有开启却要关闭一律抛错（恢复必须失败，不静默继续）。</summary>
    internal static StepMachineState ApplyBarberNightClosed(StepMachineState? state, BarberNightClosedEvent closed)
    {
        var current = StepMachineFolder.Require(state, closed);
        if (current.BarberNight is null)
        {
            throw new InvalidOperationException("事件流顺序损坏：理发师之夜事实没有开启，却要关闭");
        }

        return current with { BarberNight = null };
    }

    /// <summary>开启贤者「被恶魔杀死」事实；同一夜不能开两次（重复即事件流损坏）。</summary>
    internal static StepMachineState ApplySageNightOpened(StepMachineState? state, SageNightOpenedEvent opened)
    {
        var current = StepMachineFolder.Require(state, opened);
        if (current.SageNight is not null)
        {
            throw new InvalidOperationException("事件流顺序损坏：贤者事实已经开启过，不能重复开启");
        }

        return current with
        {
            SageNight = new SageNight
            {
                Sage = opened.Sage,
                Demon = opened.Demon,
                DemonCharacter = opened.DemonCharacter,
                Effective = opened.Effective,
                Note = opened.Note,
            },
        };
    }

    /// <summary>关闭贤者事实：没有开启却要关闭一律抛错（恢复必须失败，不静默继续）。</summary>
    internal static StepMachineState ApplySageNightClosed(StepMachineState? state, SageNightClosedEvent closed)
    {
        var current = StepMachineFolder.Require(state, closed);
        if (current.SageNight is null)
        {
            throw new InvalidOperationException("事件流顺序损坏：贤者事实没有开启，却要关闭");
        }

        return current with { SageNight = null };
    }

    /// <summary>
    /// 记录呆瓜的选择；同一名呆瓜至多两条（咖啡师「行动两次」会让他选两次，R-0052 第 3 条：
    /// 「他只选了几次」由触发层按窗口判定，这里只守住"不会更多"的硬上限）。
    /// </summary>
    internal static StepMachineState ApplyKlutzChoice(StepMachineState? state, KlutzChoiceMadeEvent choice)
    {
        var current = StepMachineFolder.Require(state, choice);
        if (current.KlutzChoices.Count(record => record.Klutz == choice.Klutz) >= 2)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：席位 {choice.Klutz.Value} 的呆瓜选择已经记录过两次（上限）");
        }

        return current with
        {
            KlutzChoices =
            [
                .. current.KlutzChoices,
                new KlutzChoiceRecord
                {
                    Klutz = choice.Klutz,
                    Target = choice.Target,
                    Detail = $"呆瓜（{choice.Klutz.Value} 号）公开选择了 {choice.Target.Value} 号",
                },
            ],
        };
    }

    /// <summary>
    /// 记录呆瓜"没有选择"（能力未生效 / 被作废）；同一名呆瓜至多两条（上限与
    /// <see cref="ApplyKlutzChoice"/> 同一口径：两次选择的窗口下第一遍也可能被作废）。
    /// </summary>
    internal static StepMachineState ApplyKlutzChoiceSkipped(
        StepMachineState? state,
        KlutzChoiceSkippedEvent skipped)
    {
        var current = StepMachineFolder.Require(state, skipped);
        if (current.KlutzChoices.Count(record => record.Klutz == skipped.Klutz) >= 2)
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：席位 {skipped.Klutz.Value} 的呆瓜选择已经记录过两次（上限）");
        }

        return current with
        {
            KlutzChoices =
            [
                .. current.KlutzChoices,
                new KlutzChoiceRecord
                {
                    Klutz = skipped.Klutz,
                    Detail = skipped.Reason,
                },
            ],
        };
    }

    /// <summary>
    /// 记录心上人的一次「没有产生效果」（能力未生效 / 说书人未裁定）；同一名心上人只能有一条记录
    /// （首版没有复活类机制，死亡是一次性的；将来引入复活时按 R-0027 第 1 条同款重做判据）。
    /// </summary>
    internal static StepMachineState ApplySweetheartSkip(
        StepMachineState? state,
        SweetheartDeathSkippedEvent skipped)
    {
        var current = StepMachineFolder.Require(state, skipped);
        if (current.SweetheartSkips.Any(record => record.Sweetheart == skipped.Sweetheart))
        {
            throw new InvalidOperationException(
                $"事件流顺序损坏：席位 {skipped.Sweetheart.Value} 的心上人死亡触发已经记录过");
        }

        return current with
        {
            SweetheartSkips =
            [
                .. current.SweetheartSkips,
                new SweetheartSkipRecord
                {
                    Sweetheart = skipped.Sweetheart,
                    Reason = skipped.Reason,
                },
            ],
        };
    }
}
