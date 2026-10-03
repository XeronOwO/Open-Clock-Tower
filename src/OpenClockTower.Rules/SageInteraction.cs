using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 贤者之夜的展示交互：贤者触发格进入时开「两名玩家」的原子选择裁定点，
/// 结清后把说书人的选择翻译成信息结果、只发给贤者本人。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="SageNightTrigger"/> 分工：触发器负责「按新事件求后果」的路由与死亡记账，
/// 这里负责交互本身（候选集合、裁定点、信息内容）。两者都只读账、只产事件，不写账
/// （<see cref="IEventTrigger"/> 的硬约定）。平台口径见 <c>docs/standard/rulings.md</c> R-0038。
/// </para>
/// <para>
/// 依据（均为百科 · 2026-10-01 抓取）：《贤者》· 角色能力——「在**当晚**你会被唤醒并得知两名玩家，
/// 其中一名是杀死你的那个恶魔」；· 运作方式——唤醒贤者、指向两名玩家，两名可以是存活或已死亡玩家。
/// 平台不硬校验展示组合必须含击杀者（信息内容由说书人给出，D-0002），只把推演写进提示与注记。
/// </para>
/// </remarks>
internal static class SageInteraction
{
    /// <summary>开展示交互：事实在、当夜贤者格进入时开一次说书人裁定点（幂等三重判据见类注）。</summary>
    internal static void TryOpen(
        EventTriggerContext context,
        List<GameEvent> events,
        ref bool interactionHandled)
    {
        if (interactionHandled || context.Machine is not { } machine)
        {
            return;
        }

        if (CurrentFact(context, events) is not { } fact || IsInteractionPending(machine, events))
        {
            return;
        }

        var slotId = TrySageSlotId(machine)
            ?? throw new InvalidOperationException("贤者之夜：当前计划里没有贤者触发格（数据缺陷）");

        var prompt = BuildPrompt(context, fact);
        if (prompt.Options.Count == 0)
        {
            // 座次不足（至少要有贤者之外的两个人）属配置缺陷：不挂一个没人能答的裁定点。
            events.Add(new SageNightClosedEvent
            {
                Note = $"座次名单不足以选出两名玩家（当前 {context.Seats.Count} 席）：展示无法进行，事实关闭",
            });
            return;
        }

        interactionHandled = true;
        events.Add(new DecisionPointRaisedEvent
        {
            SlotId = slotId,

            // 归属 = 死亡时点以贤者身份落账的席位（fact.Sage）：触发格没有行动者，只有它有。
            AttributionSeat = fact.Sage,
            DecisionPoint = new DecisionPoint
            {
                Id = PairDecisionId(machine, slotId),
                Prompt = prompt,
            },
        });
    }

    /// <summary>裁定结清：解析两名玩家 → 信息结果（只到贤者本人）→ 关闭事实；未裁定则显式关闭事实。</summary>
    internal static void HandleResolved(
        EventTriggerContext context,
        DecisionPointResolvedEvent resolved,
        List<GameEvent> events)
    {
        if (context.Machine is not { } machine
            || CurrentFact(context, events) is not { } fact
            || IsClosedInBatch(context, events)
            || TrySageSlotId(machine) is not { } slotId
            || PairDecisionId(machine, slotId) != resolved.DecisionPointId)
        {
            return;
        }

        if (resolved.Decision is null)
        {
            events.Add(new SageNightClosedEvent
            {
                Note = $"说书人没有完成贤者（{fact.Sage.Value} 号）的当晚展示"
                    + $"（{resolved.Note ?? "未说明"}）：不发信息，事实关闭",
            });
            return;
        }

        var pair = PlayerPairChoice.ParsePair(resolved.Decision)
            ?? throw new InvalidOperationException($"贤者的展示裁定不是合法玩家对：{resolved.Decision}");
        ValidatePair(context, fact, pair);

        var vortox = VortoxInterference.IsActive(context.State);
        events.Add(new InformationResultIssuedEvent
        {
            Recipient = fact.Sage,
            Ability = SageAbility.InfoAbility,
            Content = SageAbility.ComposeContent(pair.First, pair.Second),
            MayBeFalse = fact.Effective != true || vortox,
            Note = $"{SageAbility.EffectivenessNote(fact.Effective)}；按击杀记录推演：杀死贤者的是 "
                + $"{fact.Demon.Value} 号（{fact.DemonCharacter.Value}）"
                + (vortox ? "；涡流在场：这条信息必须为假（R-0028）" : string.Empty),
        });
        events.Add(new SageNightClosedEvent
        {
            Note = $"贤者（{fact.Sage.Value} 号）的当晚展示完成：{pair.First.Value} 号 与 {pair.Second.Value} 号"
                + "（信息只发给贤者本人）",
        });
    }

    /// <summary>贤者触发格在计划里的下标；没有（首夜 / 非夜晚计划）时返回 null。</summary>
    internal static int? TrySageSlotIndex(StepMachineState machine)
    {
        if (machine.Plan.Phase is not (GamePhase.FirstNight or GamePhase.OtherNight))
        {
            return null;
        }

        for (var index = 0; index < machine.Plan.Slots.Count; index++)
        {
            var slot = machine.Plan.Slots[index];
            if (slot.Kind == StepSlotKind.Trigger && slot.Character == SageAbility.Character)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>当前挂着的事实：本批已产出的事件优先，其次步骤机状态。</summary>
    private static SageNight? CurrentFact(EventTriggerContext context, IReadOnlyList<GameEvent> produced)
    {
        for (var index = produced.Count - 1; index >= 0; index--)
        {
            switch (produced[index])
            {
                case SageNightClosedEvent:
                    return null;
                case SageNightOpenedEvent opened:
                    return new SageNight
                    {
                        Sage = opened.Sage,
                        Demon = opened.Demon,
                        DemonCharacter = opened.DemonCharacter,
                        Effective = opened.Effective,
                        Note = opened.Note,
                    };
            }
        }

        return context.Machine?.SageNight;
    }

    /// <summary>同一夜是否已经开过交互（等待裁定 / 本批已产出）。</summary>
    private static bool IsInteractionPending(StepMachineState machine, IReadOnlyList<GameEvent> produced)
    {
        if (TrySageSlotId(machine) is not { } slotId)
        {
            return false;
        }

        var decisionId = PairDecisionId(machine, slotId);
        if (machine.AwaitingDecision is { } decision && decision.Id == decisionId)
        {
            return true;
        }

        return produced.OfType<DecisionPointRaisedEvent>()
            .Any(raised => raised.DecisionPoint.Id == decisionId);
    }

    /// <summary>本批里是否已经有关闭事件（例如夜晚收口与裁定结清同批）：有就不再补第二条。</summary>
    private static bool IsClosedInBatch(EventTriggerContext context, IReadOnlyList<GameEvent> produced) =>
        context.Events.OfType<SageNightClosedEvent>().Any() || produced.OfType<SageNightClosedEvent>().Any();

    /// <summary>
    /// 展示组合的迟到校验：两名玩家都在座次名单里、且不含贤者本人
    /// （说书人裁定值不经过选项闸的精确匹配，内核必须自己拒绝越界编码——与理发师同族）。
    /// </summary>
    private static void ValidatePair(EventTriggerContext context, SageNight fact, (SeatId First, SeatId Second) pair)
    {
        foreach (var seat in new[] { pair.First, pair.Second })
        {
            if (!context.Seats.Contains(seat))
            {
                throw new InvalidOperationException(
                    $"贤者展示里的席位 {seat.Value} 不在本局座次名单里：裁定编码无效");
            }

            if (seat == fact.Sage)
            {
                throw new InvalidOperationException(
                    $"贤者展示不能把贤者本人（{fact.Sage.Value} 号）列为候选之一：裁定编码无效");
            }
        }
    }

    /// <summary>候选 = 除贤者自己外的任意两名玩家（含已死亡玩家；百科《贤者》运作方式）。</summary>
    private static ChoicePrompt BuildPrompt(EventTriggerContext context, SageNight fact)
    {
        var others = context.Seats
            .Where(seat => seat != fact.Sage)
            .OrderBy(seat => seat.Value)
            .ToArray();
        var options = new List<DecisionOption>();
        for (var first = 0; first < others.Length; first++)
        {
            for (var second = first + 1; second < others.Length; second++)
            {
                options.Add(new DecisionOption
                {
                    Value = PlayerPairChoice.FormatPair(others[first], others[second]),
                    Preview = $"{others[first].Value} 号 + {others[second].Value} 号",
                });
            }
        }

        return new ChoicePrompt
        {
            Context = $"贤者（{fact.Sage.Value} 号）被恶魔杀死：请选择展示给他的两名玩家"
                + "（百科《贤者》· 2026-10-01 抓取 · 运作方式）。"
                + $"按击杀记录推演：杀死他的是 {fact.Demon.Value} 号（{fact.DemonCharacter.Value}）"
                + "——能力生效时，展示的两名玩家中应包含该席位。"
                + SageAbility.EffectivenessNote(fact.Effective)
                + (VortoxInterference.IsActive(context.State)
                    ? "涡流在场：这条信息必须为假（R-0028）——展示与推演不同的内容。"
                    : string.Empty),
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <summary>贤者触发格的槽位标识；当前计划不是夜晚或没有这一格时返回 null。</summary>
    private static StepSlotId? TrySageSlotId(StepMachineState machine) =>
        TrySageSlotIndex(machine) is { } index ? machine.Plan.Slots[index].Id : null;

    /// <summary>派生稳定裁定点标识：计划 + 贤者格（同一夜唯一）。</summary>
    private static DecisionPointId PairDecisionId(StepMachineState machine, StepSlotId slotId) =>
        new($"sage:{machine.Plan.Label}:{slotId.Value}:pair");
}
