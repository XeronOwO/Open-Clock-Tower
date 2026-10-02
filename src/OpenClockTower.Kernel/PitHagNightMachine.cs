namespace OpenClockTower.Kernel;

/// <summary>
/// 麻脸巫婆之夜的死亡裁量：说书人的两条命令（追加死亡 / 裁定待定死亡）与死亡事实的产出。
/// </summary>
/// <remarks>
/// <para>
/// 从 <see cref="StepMachine"/> 拆出（单文件 600 行门禁；与 <see cref="AdjudicatedExecutionMachine"/>
/// 同族——说书人的主动命令各有一台小机器）。口径见 <c>docs/standard/rulings.md</c> R-0030：
/// 追加死亡归因为麻脸巫婆；确认的待定死亡归因为发起击杀的恶魔。
/// </para>
/// <para>
/// 窗口的开启由麻脸巫婆的结算契约产出（<see cref="PitHagNightOpenedEvent"/>），
/// 收口由推进路径完成（越过最后一个能造成死亡的恶魔行动 → 未裁定的按默认结果生效）。
/// </para>
/// </remarks>
internal static class PitHagNightMachine
{
    /// <summary>
    /// 追加死亡（R-0030 第 4 条）：说书人在窗口期内让某名玩家死亡，归因为**麻脸巫婆**
    /// （不触发"被恶魔杀死"类能力）。
    /// </summary>
    internal static StepMachineOutcome HandleCasualty(
        StepMachineState state,
        SettlementContext context,
        PitHagCasualtyInput input)
    {
        if (state.PitHagNight is not { } night)
        {
            return Reject(
                state,
                StepMachineRejectionReason.NoPitHagNight,
                "今晚没有麻脸巫婆的死亡裁量窗口（还没创造出恶魔，或窗口已经关闭）");
        }

        var life = context.State.Seat(input.Target)?.LifeValue;
        if (life is null)
        {
            return Reject(
                state,
                StepMachineRejectionReason.LedgerIncomplete,
                $"席位 {input.Target.Value} 的生死还没有观测，追加死亡不替它猜（D-0015）");
        }

        if (life == LifeState.Dead)
        {
            return Reject(
                state,
                StepMachineRejectionReason.UnexpectedInput,
                $"席位 {input.Target.Value} 已经死亡，追加死亡没有意义");
        }

        var effectId = new EffectId($"{night.CasualtyAbility.Value}:{input.Target.Value}:{state.SlotIndex}");
        return Applied(
            state,
            [
                new InstantaneousEffectAppliedEvent
                {
                    Effect = new InstantaneousEffect
                    {
                        Id = effectId,
                        Source = night.Source,
                        Ability = night.CasualtyAbility,
                        Target = input.Target,
                    },
                },
                new SeatStateChangedEvent
                {
                    Seat = input.Target,
                    Life = LifeState.Dead,
                    Reason = input.Note is { Length: > 0 } note
                        ? $"麻脸巫婆造成死亡：{note}"
                        : "麻脸巫婆造成死亡（说书人裁定）",
                    CausedBy = night.Source,
                    EffectId = effectId,
                },
            ]);
    }

    /// <summary>
    /// 裁定一条待定死亡（R-0030 第 2 条）：确认则落死亡事实（归因为发起击杀的恶魔），
    /// 阻止则只留裁定——「说书人能让原本被恶魔攻击且会死亡的玩家免死」（百科《免死》）。
    /// </summary>
    internal static StepMachineOutcome HandleResolve(
        StepMachineState state,
        SettlementContext context,
        ResolveDeferredDeathInput input)
    {
        if (state.PitHagNight is not { } night)
        {
            return Reject(
                state,
                StepMachineRejectionReason.NoPitHagNight,
                "今晚没有麻脸巫婆的死亡裁量窗口（还没创造出恶魔，或窗口已经关闭）");
        }

        var deferred = night.Deferred.FirstOrDefault(item => item.Target == input.Target);
        if (deferred is null)
        {
            return Reject(
                state,
                StepMachineRejectionReason.UnexpectedInput,
                $"席位 {input.Target.Value} 没有待定的死亡");
        }

        var events = new List<GameEvent>
        {
            new DeferredDeathResolvedEvent
            {
                Target = input.Target,
                Killed = input.Killed,
                Note = input.Note is { Length: > 0 } note
                    ? note
                    : input.Killed ? "说书人确认死亡" : "说书人阻止死亡（免死）",
            },
        };

        if (input.Killed)
        {
            AppendOutcome(events, context.State, deferred, "说书人确认死亡");
        }

        return Applied(state, events);
    }

    /// <summary>
    /// 把一条待定死亡按它的载荷落成事实：携带「转化」载荷的按转化结算（方古侵染），
    /// 否则按普通击杀。说书人「确认」与窗口关闭的默认结果都走这里——口径必须只有一处。
    /// </summary>
    /// <remarks>
    /// 依据：百科《方古》· 2026-10-01 抓取 · 角色简介 2（「改为方古死亡，外来者变成邪恶的方古」）；
    /// 平台口径见 <c>docs/standard/rulings.md</c> R-0034。
    /// </remarks>
    internal static void AppendOutcome(
        List<GameEvent> events,
        GameState ledger,
        DeferredDeath deferred,
        string note)
    {
        if (deferred.Transformation is { } transformation)
        {
            AppendTransformation(events, ledger, deferred, transformation, note);
            return;
        }

        AppendKill(events, ledger, deferred, note);
    }

    /// <summary>
    /// 把一条「转化」载荷落成事实：目标变成新的邪恶方古、原方古死亡、「限一次」标记落下；
    /// **被攻击的外来者不死亡**。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 依据：百科《方古》· 2026-10-01 抓取 · 角色简介 2 / 运作方式 11–14；
    /// 平台口径（确认 = 按能力转化、阻止 = 两者都不发生）见 <c>docs/standard/rulings.md</c> R-0034。
    /// </para>
    /// <para>
    /// 目标在确认时已经死亡 → **整体不发生**：没有「成功杀死」这回事，方古也不死
    /// （百科《方古》· 2026-10-01 抓取 · 范例：方古攻击已死亡的呆瓜，「因为呆瓜已经死了，
    /// 他不会再次死亡，所以方古不会死」）。原方古若已经死亡，不再重复记一条死亡事实。
    /// </para>
    /// </remarks>
    internal static void AppendTransformation(
        List<GameEvent> events,
        GameState ledger,
        DeferredDeath deferred,
        DeferredTransformation transformation,
        string note)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(deferred);
        ArgumentNullException.ThrowIfNull(transformation);

        if (ledger.Seat(transformation.Target)?.LifeValue == LifeState.Dead)
        {
            return;
        }

        var effectId = new EffectId($"{deferred.Ability.Value}:{transformation.Target.Value}:infection");
        events.Add(new InstantaneousEffectAppliedEvent
        {
            Effect = new InstantaneousEffect
            {
                Id = effectId,
                Source = deferred.Source,
                Ability = deferred.Ability,
                Target = transformation.Target,
            },
        });
        events.Add(new SeatStateChangedEvent
        {
            Seat = transformation.Target,
            Character = transformation.Character,
            Alignment = transformation.Alignment,
            Reason = $"方古侵染（麻脸巫婆之夜由说书人裁定：{note}）：{transformation.Note}",
            CausedBy = deferred.Source,
            EffectId = effectId,
        });

        if (ledger.Seat(transformation.Dies)?.LifeValue != LifeState.Dead)
        {
            events.Add(new SeatStateChangedEvent
            {
                Seat = transformation.Dies,
                Life = LifeState.Dead,
                Reason = $"方古侵染（麻脸巫婆之夜由说书人裁定：{note}）：原方古死亡",
                CausedBy = deferred.Source,
                EffectId = effectId,
            });
        }

        events.Add(new FangGuInfectionRecordedEvent
        {
            Seat = transformation.Target,
            Source = deferred.Source,
        });
    }

    /// <summary>
    /// 把一条待定死亡落成死亡事实（效果 + 状态变化）；目标已经死亡时只保留裁定本身。
    /// </summary>
    internal static void AppendKill(
        List<GameEvent> events,
        GameState ledger,
        DeferredDeath deferred,
        string note)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(deferred);

        if (ledger.Seat(deferred.Target)?.LifeValue == LifeState.Dead)
        {
            return;
        }

        var effectId = new EffectId($"{deferred.Ability.Value}:{deferred.Target.Value}:deferred");
        events.Add(new InstantaneousEffectAppliedEvent
        {
            Effect = new InstantaneousEffect
            {
                Id = effectId,
                Source = deferred.Source,
                Ability = deferred.Ability,
                Target = deferred.Target,
            },
        });
        events.Add(new SeatStateChangedEvent
        {
            Seat = deferred.Target,
            Life = LifeState.Dead,
            Reason = $"恶魔击杀（麻脸巫婆之夜由说书人裁定：{note}）",
            CausedBy = deferred.Source,
            EffectId = effectId,
        });
    }

    private static StepMachineOutcome Applied(StepMachineState state, List<GameEvent> events) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Applied,
            State = StepMachineFolder.ApplyAll(state, events)
                ?? throw new InvalidOperationException("事件流损坏：处理输入后丢失步骤机状态"),
            Events = events,
        };

    private static StepMachineOutcome Reject(
        StepMachineState state,
        StepMachineRejectionReason reason,
        string note) =>
        new()
        {
            Kind = StepMachineOutcomeKind.Rejected,
            State = state,
            Events = [],
            RejectionReason = reason,
            RejectionNote = note,
        };
}
