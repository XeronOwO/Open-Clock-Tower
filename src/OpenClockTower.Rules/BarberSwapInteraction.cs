using System.Globalization;
using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 理发师之夜的交互机制：理发师触发格进入时开「玩家对 / 不交换」请求（多恶魔先说书人裁定），
/// 应答 / 作废后完成角色交换与槽位重绑并收口事实。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="BarberNightTrigger"/> 分工：触发器负责「按新事件求后果」的路由与死亡记账，
/// 这里负责交互本身（候选集合、操作请求、交换结算）。两者都只读账、只产事件，不写账
/// （<see cref="IEventTrigger"/> 的硬约定）。
/// </para>
/// <para>
/// 依据（均为百科 · 2026-10-01 抓取）：《理发师》· 运作方式——「在理发师死亡的夜晚，唤醒一名恶魔玩家」、
/// 恶魔摇头 = 不交换、不能选择另一名恶魔、可选自己与已死亡玩家；· 角色能力——交换两名玩家的**角色**；
/// 《夜晚行动顺序一览》· 其他夜晚——理发师格在恶魔段之后。平台口径见
/// <c>docs/standard/rulings.md</c> R-0033。
/// </para>
/// </remarks>
internal static class BarberSwapInteraction
{
    /// <summary>开交互：存活恶魔一名 → 直接开请求；多名 → 先说书人裁定；一名都没有 → 显式收口事实。</summary>
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

        interactionHandled = true;

        var demons = LivingDemons(context);
        if (demons.Count == 0)
        {
            events.Add(new BarberNightClosedEvent
            {
                Note = "理发师之夜：场上没有存活恶魔，交换无法进行（事实关闭）",
            });
            return;
        }

        if (demons.Count > 1)
        {
            events.Add(new DecisionPointRaisedEvent
            {
                SlotId = TryBarberSlotId(machine)
                    ?? throw new InvalidOperationException("理发师之夜：当前计划里没有理发师触发格（数据缺陷）"),

                // 归属 = 以理发师身份死亡的席位（触发格没有行动者）。
                AttributionSeat = fact.Source,
                DecisionPoint = new DecisionPoint
                {
                    Id = TryDemonChoiceId(machine)
                        ?? throw new InvalidOperationException("理发师之夜：当前计划里没有理发师触发格（数据缺陷）"),
                    Prompt = BuildDemonChoicePrompt(demons),
                },
            });
            return;
        }

        IssueRequest(context, machine, demons[0], events);
    }

    /// <summary>说书人裁定「用哪名恶魔」之后开请求；未裁定（强推 / 收口）则显式关闭事实。</summary>
    internal static void HandleDemonChosen(
        EventTriggerContext context,
        DecisionPointResolvedEvent resolved,
        List<GameEvent> events,
        ref bool interactionHandled)
    {
        if (context.Machine is not { } machine
            || TryDemonChoiceId(machine) != resolved.DecisionPointId
            || IsClosedInBatch(context, events)
            || CurrentFact(context, events) is null)
        {
            return;
        }

        if (resolved.Decision is null)
        {
            events.Add(new BarberNightClosedEvent
            {
                Note = $"说书人没有裁定由哪名恶魔执行交换（{resolved.Note ?? "未说明"}）：今晚不交换",
            });
            return;
        }

        var demon = SeatChoice.Parse(resolved.Decision)
            ?? throw new InvalidOperationException($"理发师的恶魔裁定不是合法席位编码：{resolved.Decision}");

        if (!LivingDemons(context).Contains(demon))
        {
            throw new InvalidOperationException(
                $"说书人裁定的恶魔（{demon.Value} 号）不是此刻的存活恶魔：裁定无效，请重新裁定");
        }

        interactionHandled = true;
        IssueRequest(context, machine, demon, events);
    }

    /// <summary>恶魔答完：摇头 → 关闭事实；玩家对 → 两条角色变化 + 重绑，再关闭事实。</summary>
    internal static void HandleAnswer(
        EventTriggerContext context,
        OperationRequestAnsweredEvent answered,
        List<GameEvent> events)
    {
        if (context.Machine is not { } machine
            || machine.BarberNight is not { } fact
            || !TryParseBarberRequest(machine, answered.RequestId, out var demon)
            || IsClosedInBatch(context, events))
        {
            return;
        }

        if (BarberChoice.IsDecline(answered.Answer.OptionValue))
        {
            events.Add(new BarberNightClosedEvent
            {
                Note = "理发师之夜：恶魔选择不交换（摇头拒绝；百科《理发师》· 2026-10-01 抓取 · 运作方式）",
            });
            return;
        }

        var pair = BarberChoice.ParsePair(answered.Answer.OptionValue)
            ?? throw new InvalidOperationException(
                $"理发师的结算选择不是「玩家对 / 不交换」编码：{answered.Answer.OptionValue}");

        ValidatePair(context, pair, demon);

        var firstCharacter = context.State.Seat(pair.First)?.CharacterValue
            ?? throw new InvalidOperationException($"席位 {pair.First.Value} 的角色尚未观测，交换不了");
        var secondCharacter = context.State.Seat(pair.Second)?.CharacterValue
            ?? throw new InvalidOperationException($"席位 {pair.Second.Value} 的角色尚未观测，交换不了");

        events.Add(new SeatStateChangedEvent
        {
            // 只写角色维度：阵营不变（百科《理发师》机制 6；六维度相互独立）。
            Seat = pair.First,
            Character = secondCharacter,
            PreviousCharacter = firstCharacter,
            Reason = BarberAbility.SwapReason(fact.Source, demon),
            CausedBy = fact.Source,
        });
        events.Add(new SeatStateChangedEvent
        {
            Seat = pair.Second,
            Character = firstCharacter,
            PreviousCharacter = secondCharacter,
            Reason = BarberAbility.SwapReason(fact.Source, demon),
            CausedBy = fact.Source,
        });

        // 换手后尚未进入的槽位跟随新持有者（R-0032）；已经进入过的槽位不重绑（过时不候）。
        AddRebinding(context, machine, pair.First, secondCharacter, events);
        AddRebinding(context, machine, pair.Second, firstCharacter, events);

        events.Add(new BarberNightClosedEvent
        {
            Note = $"理发师之夜：恶魔（{demon.Value} 号）交换了 {pair.First.Value} 号与 {pair.Second.Value} 号的角色；"
                + $"理发师为 {fact.Source.Value} 号（阵营不变）",
        });
    }

    /// <summary>请求被作废（强推 / 依赖失效 / 收口）：今晚不交换，显式关闭事实。</summary>
    internal static void HandleVoid(
        EventTriggerContext context,
        OperationRequestVoidedEvent voided,
        List<GameEvent> events)
    {
        if (context.Machine is not { } machine
            || machine.BarberNight is null
            || !TryParseBarberRequest(machine, voided.RequestId, out _)
            || IsClosedInBatch(context, events))
        {
            return;
        }

        var note = string.IsNullOrWhiteSpace(voided.Void.Note) ? "未说明" : voided.Void.Note;
        events.Add(new BarberNightClosedEvent
        {
            Note = $"理发师之夜：请求被作废（{voided.Void.Reason}：{note}）：今晚不交换",
        });
    }

    /// <summary>理发师触发格在计划里的下标；没有（首夜 / 非夜晚计划）时返回 null。</summary>
    internal static int? TryBarberSlotIndex(StepMachineState machine)
    {
        if (machine.Plan.Phase is not (GamePhase.FirstNight or GamePhase.OtherNight))
        {
            return null;
        }

        for (var index = 0; index < machine.Plan.Slots.Count; index++)
        {
            var slot = machine.Plan.Slots[index];
            if (slot.Kind == StepSlotKind.Trigger && slot.Character == BarberAbility.Character)
            {
                return index;
            }
        }

        return null;
    }

    /// <summary>同一夜是否已经开过交互（挂起请求 / 等待裁定 / 本批已产出）。</summary>
    private static bool IsInteractionPending(StepMachineState machine, IReadOnlyList<GameEvent> produced)
    {
        if (machine.PendingRequest is { Status: OperationRequestStatus.Pending } pending
            && TryParseBarberRequest(machine, pending.Id, out _))
        {
            return true;
        }

        var decisionId = TryDemonChoiceId(machine);
        if (decisionId is { } currentDecision
            && machine.AwaitingDecision is { } decision
            && decision.Id == currentDecision)
        {
            return true;
        }

        foreach (var gameEvent in produced)
        {
            switch (gameEvent)
            {
                case OperationRequestIssuedEvent issued when TryParseBarberRequest(machine, issued.Request.Id, out _):
                    return true;
                case DecisionPointRaisedEvent raised when raised.DecisionPoint.Id == decisionId:
                    return true;
            }
        }

        return false;
    }

    /// <summary>当前挂着的事实：本批已产出的事件优先，其次步骤机状态，最后看本批「即将开出」的死亡。</summary>
    private static BarberNight? CurrentFact(EventTriggerContext context, IReadOnlyList<GameEvent> produced)
    {
        for (var index = produced.Count - 1; index >= 0; index--)
        {
            switch (produced[index])
            {
                case BarberNightClosedEvent:
                    return null;
                case BarberNightOpenedEvent opened:
                    return new BarberNight { Source = opened.Source, Note = opened.Note };
            }
        }

        if (context.Machine?.BarberNight is { } folded)
        {
            return folded;
        }

        // 同一批里「理发师格进入」可能排在死亡事件之前：按同一套判据把本批的死亡也看一遍。
        for (var index = 0; index < context.Events.Count; index++)
        {
            if (context.Events[index] is SeatStateChangedEvent { Life: LifeState.Dead } death
                && BarberNightTrigger.EvaluateDeath(context, death, index) is { Fact: { } fact })
            {
                return fact;
            }
        }

        return null;
    }

    /// <summary>开请求：把「两名玩家（不能选另一名恶魔）交换角色 / 不交换」的原子选择发给该恶魔。</summary>
    private static void IssueRequest(
        EventTriggerContext context,
        StepMachineState machine,
        SeatId demon,
        List<GameEvent> events)
    {
        var slotId = TryBarberSlotId(machine)
            ?? throw new InvalidOperationException("理发师之夜：当前计划里没有理发师触发格（数据缺陷）");
        var demonCharacter = context.State.Seat(demon)?.CharacterValue
            ?? throw new InvalidOperationException($"恶魔席位 {demon.Value} 的角色尚未观测，开不出交换请求");

        events.Add(new OperationRequestIssuedEvent
        {
            Request = new OperationRequest
            {
                Id = RequestIdFor(machine, demon),
                Addressee = demon,
                // 槽位来源：请求挂在理发师格上——由步骤机保证「挂着不推进、答完继续走」，
                // 而结算（角色交换）由本交互从「答了什么」的事件里产出（格子上没有行动契约）。
                Origin = OperationRequestOrigin.ForSlot(slotId, machine.Plan.Label, SlotIndexOf(machine, slotId)),
                Prompt = BuildSwapPrompt(context, demon),
                Dependencies =
                [
                    new SeatDependency
                    {
                        Seat = demon,
                        RequiredLife = LifeState.Alive,
                        RequiredCharacter = demonCharacter,
                    },
                ],
            },
        });
    }

    /// <summary>玩家对 / 不交换的原子选择：合法玩家对按席位升序 + 「不交换」（摇头）。</summary>
    private static ChoicePrompt BuildSwapPrompt(EventTriggerContext context, SeatId demon)
    {
        var seats = context.Seats.OrderBy(seat => seat.Value).ToArray();
        var options = new List<DecisionOption>();
        for (var first = 0; first < seats.Length; first++)
        {
            for (var second = first + 1; second < seats.Length; second++)
            {
                if (!IsSelectable(context, seats[first], demon) || !IsSelectable(context, seats[second], demon))
                {
                    continue;
                }

                options.Add(new DecisionOption
                {
                    Value = BarberChoice.FormatPair(seats[first], seats[second]),
                    Preview = $"{seats[first].Value} 号 ↔ {seats[second].Value} 号",
                });
            }
        }

        options.Add(new DecisionOption
        {
            Value = BarberChoice.Decline,
            Preview = "不交换（摇头拒绝）",
        });

        return new ChoicePrompt
        {
            Context = "理发师死亡触发：恶魔选择两名玩家交换角色（阵营不变；可选自己与已死亡玩家，"
                + "不能选另一名恶魔）——「不交换」是摇头的等价物（百科《理发师》· 2026-10-01 抓取 · 运作方式）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <summary>这名玩家是否可选：执行交换的恶魔自己可以；其他恶魔玩家一律排除。</summary>
    private static bool IsSelectable(EventTriggerContext context, SeatId seat, SeatId demon)
    {
        var entry = context.State.Seat(seat)
            ?? throw new InvalidOperationException($"理发师请求的候选席位 {seat.Value} 不在状态账里");
        var character = entry.CharacterValue
            ?? throw new InvalidOperationException(
                $"席位 {seat.Value} 的角色尚未观测：算不出合法玩家对，不猜（D-0015）");

        return seat == demon || SectsAndVioletsRoster.TypeOf(character) != CharacterType.Demon;
    }

    /// <summary>此刻存活且持有恶魔角色的玩家（按席位升序）；生死未观测显式失败，不猜。</summary>
    private static IReadOnlyList<SeatId> LivingDemons(EventTriggerContext context)
    {
        var demons = new List<SeatId>();
        foreach (var seat in context.Seats.OrderBy(seat => seat.Value))
        {
            var entry = context.State.Seat(seat);
            if (entry?.CharacterValue is not { } character
                || SectsAndVioletsRoster.TypeOf(character) != CharacterType.Demon)
            {
                continue;
            }

            if (entry.LifeValue is null)
            {
                throw new InvalidOperationException(
                    $"席位 {seat.Value} 的角色是恶魔（{character.Value}），但生死还没有观测："
                    + "算不出存活恶魔集合，不猜（D-0015）");
            }

            if (entry.LifeValue == LifeState.Alive)
            {
                demons.Add(seat);
            }
        }

        return demons;
    }

    /// <summary>多名存活恶魔：说书人先裁定由哪名恶魔执行交换（机制 4）。</summary>
    private static ChoicePrompt BuildDemonChoicePrompt(IReadOnlyList<SeatId> demons) =>
        new()
        {
            Context = "理发师死亡触发：场上有多名存活恶魔——说书人先选择由哪名恶魔执行今晚的角色交换"
                + "（百科《理发师》· 2026-10-01 抓取 · 运作方式）",
            Options =
            [
                .. demons.Select(seat => new DecisionOption
                {
                    Value = SeatChoice.Format(seat),
                    Preview = $"{seat.Value} 号恶魔",
                }),
            ],
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };

    /// <summary>校验迟到的答案仍是合法玩家对：两名玩家都在账上，且没有「另一名恶魔」。</summary>
    private static void ValidatePair(EventTriggerContext context, (SeatId First, SeatId Second) pair, SeatId demon)
    {
        foreach (var seat in new[] { pair.First, pair.Second })
        {
            var entry = context.State.Seat(seat)
                ?? throw new InvalidOperationException($"理发师选择的席位 {seat.Value} 不在状态账里");
            var character = entry.CharacterValue
                ?? throw new InvalidOperationException(
                    $"席位 {seat.Value} 的角色尚未观测：判不了是不是「另一名恶魔」，不猜（D-0015）");

            if (seat != demon && SectsAndVioletsRoster.TypeOf(character) == CharacterType.Demon)
            {
                throw new InvalidOperationException(
                    $"理发师不能选择另一名恶魔（{seat.Value} 号是 {character.Value}）：这条答案无效，请重新选择");
            }
        }
    }

    /// <summary>把「新持有者 + 新角色」接到尚未进入的槽位上（R-0032 的共用实现）。</summary>
    private static void AddRebinding(
        EventTriggerContext context,
        StepMachineState machine,
        SeatId seat,
        CharacterId character,
        List<GameEvent> events)
    {
        if (NightSlotActivation.Plan(
                machine.Plan,
                machine.SlotIndex,
                seat,
                character,
                context.State,
                machine.Day?.LastClosedDay,
                context.Seats,
                NightActions.Default) is { } rebound)
        {
            events.Add(rebound);
        }
    }

    /// <summary>本批里是否已经有关闭事件（例如夜晚收口与请求作废同批）：有就不再补第二条。</summary>
    private static bool IsClosedInBatch(EventTriggerContext context, IReadOnlyList<GameEvent> produced) =>
        context.Events.OfType<BarberNightClosedEvent>().Any() || produced.OfType<BarberNightClosedEvent>().Any();

    /// <summary>理发师触发格的槽位标识；当前计划不是夜晚或没有这一格时返回 null。</summary>
    private static StepSlotId? TryBarberSlotId(StepMachineState machine) =>
        TryBarberSlotIndex(machine) is { } index ? machine.Plan.Slots[index].Id : null;

    /// <summary>
    /// 派生稳定请求标识：计划 + 理发师格 + **执行交换的恶魔席**（同一夜唯一）。
    /// </summary>
    /// <remarks>
    /// 把恶魔编进标识，是为了在请求答完、计划已经推进（挂起请求随之被清空）之后，
    /// 仍能从「答了什么 / 作废了什么」的事件里认领出是哪一名恶魔在交换——
    /// 不依赖可能已被 <see cref="SlotEnteredEvent"/> 清空的挂起态。
    /// </remarks>
    private static OperationRequestId RequestIdFor(StepMachineState machine, SeatId demon) =>
        TryRequestPrefix(machine) is { } prefix
            ? new OperationRequestId($"{prefix}{demon.Value}")
            : throw new InvalidOperationException("理发师之夜：当前计划里没有理发师触发格（数据缺陷）");

    /// <summary>认领一条属于本触发器的请求；是自家请求时给出执行交换的恶魔席位。</summary>
    private static bool TryParseBarberRequest(
        StepMachineState machine,
        OperationRequestId requestId,
        out SeatId demon)
    {
        demon = default;
        if (TryRequestPrefix(machine) is not { } prefix)
        {
            return false;
        }

        if (!requestId.Value.StartsWith(prefix, StringComparison.Ordinal)
            || !int.TryParse(
                requestId.Value.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seat)
            || seat <= 0)
        {
            return false;
        }

        demon = new SeatId(seat);
        return true;
    }

    /// <summary>请求标识前缀（计划 + 理发师格）；当前计划没有理发师格时返回 null。</summary>
    private static string? TryRequestPrefix(StepMachineState machine) =>
        TryBarberSlotId(machine) is { } slotId
            ? $"barber:{machine.Plan.Label}:{slotId.Value}:"
            : null;

    /// <summary>派生稳定裁定点标识：计划 + 理发师格（同一夜唯一）。</summary>
    private static DecisionPointId? TryDemonChoiceId(StepMachineState machine) =>
        TryBarberSlotId(machine) is { } slotId
            ? new DecisionPointId($"barber:{machine.Plan.Label}:{slotId.Value}:demon")
            : null;

    /// <summary>槽位下标：计划随事件流走，按标识查找而不是记住。</summary>
    private static int SlotIndexOf(StepMachineState machine, StepSlotId slotId)
    {
        for (var index = 0; index < machine.Plan.Slots.Count; index++)
        {
            if (machine.Plan.Slots[index].Id == slotId)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"理发师之夜：计划里找不到槽位 {slotId.Value}（数据缺陷）");
    }
}
