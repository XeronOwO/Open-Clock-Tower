using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 心上人的死亡触发：以心上人身份死亡（任何死因）时**立即**开「选择一名玩家醉酒」的
/// 触发型裁定点；结清后施加持续醉酒效果（来源换角时终止）。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为百科 · 2026-10-01 抓取）：《心上人》· 角色能力——「当你死亡时，会有一名玩家开始醉酒」；
/// · 角色简介——「一名玩家会在剩余的游戏时间里醉酒」「由说书人选择哪名玩家醉酒」；
/// · 运作方式 4——「如果心上人死亡，你选择任一玩家醉酒，为他放置『醉酒』提示标记」；
/// · 提示标记「醉酒」——放置条件「心上人死亡，且此时心上人未醉酒中毒」、移除时机「心上人离场时」；
/// 《死亡触发能力》· 能力简介——死亡时立即触发；本能力的执行者是说书人（不是让受影响的玩家做选择），
/// 因此不适用「等到夜晚的交互」。平台口径见 <c>docs/standard/rulings.md</c> R-0039。
/// </para>
/// <para>
/// **幂等**：跳过账（<see cref="StepMachineState.SweetheartSkips"/>）+ 挂起裁定点 + 效果账
/// 三重判据；事件流重放不重复开裁定、不重复施加效果。
/// </para>
/// </remarks>
internal sealed class SweetheartDeathTrigger : IEventTrigger
{
    /// <inheritdoc />
    public AbilityId Ability => SweetheartAbility.DeathAbility;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Evaluate(EventTriggerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 已经结束：不再开任何选择（《处决》第 3 步先于第 4 步；与呆瓜同族的第二道保险，R-0024）。
        if (context.Machine?.Outcome is not null)
        {
            return [];
        }

        var events = new List<GameEvent>();
        for (var index = 0; index < context.Events.Count; index++)
        {
            switch (context.Events[index])
            {
                case SeatStateChangedEvent { Life: LifeState.Dead } death:
                    HandleDeath(context, death, index, events);
                    break;

                case DecisionPointResolvedEvent resolved:
                    HandleResolved(context, resolved, events);
                    break;
            }
        }

        return events;
    }

    /// <summary>死亡事件 → 开触发型裁定点，或写一条显式的跳过。</summary>
    private static void HandleDeath(
        EventTriggerContext context,
        SeatStateChangedEvent death,
        int index,
        List<GameEvent> events)
    {
        var diedAsSweetheart = DeathTriggerReadings.CharacterAt(context, death.Seat, index);
        if (diedAsSweetheart is null)
        {
            // 本批有该席位的角色变化、却缺「变化前角色」：判不了死亡时是不是心上人——
            // 不猜，也不静默（与理发师 / 贤者同族）。
            var hasCharacterChange = context.Events.Any(gameEvent =>
                gameEvent is SeatStateChangedEvent { Character: not null } change && change.Seat == death.Seat);
            if (hasCharacterChange)
            {
                events.Add(new SweetheartDeathSkippedEvent
                {
                    Sweetheart = death.Seat,
                    Reason = "死亡批里该席位有角色变化、却缺少「变化前角色」：判不了死亡时是不是心上人，"
                        + "不猜也不静默（如确为心上人死亡，请上报角色变化时带上变化前角色）",
                });
            }

            return;
        }

        if (diedAsSweetheart != SweetheartAbility.Character)
        {
            // 不是作为心上人死亡：与本触发器无关。
            return;
        }

        if (IsDeathHandled(context, death.Seat, events))
        {
            return;
        }

        // 同批 / 跨事件的第二条心上人死亡：步骤机同一时刻只能挂一个裁定点，不静默覆盖。
        if (HasPendingSweetheartDecision(context, events))
        {
            events.Add(new SweetheartDeathSkippedEvent
            {
                Sweetheart = death.Seat,
                Reason = "已经有一条未了结的心上人裁定（说书人尚未选择醉酒目标）："
                    + "本条死亡不再开（首版不排队，显式记录；R-0039）",
            });
            return;
        }

        // 其他来源的裁定点挂着时同样不覆盖（步骤机同时只挂一个）：显式记录，不静默丢一条挂起。
        if (context.Machine?.AwaitingDecision is not null)
        {
            events.Add(new SweetheartDeathSkippedEvent
            {
                Sweetheart = death.Seat,
                Reason = "已有另一条裁定点未了结（步骤机同时只挂一个）："
                    + "本条死亡不再开（首版不排队，显式记录；R-0039）",
            });
            return;
        }

        // 生效判定读**批后账**（与角色维度不同源：角色维度按事件序重建）。真机上该席位的
        // 醉酒 / 中毒变化都由独立批次产出（上报 / 对账），因此批后账 = 死亡时刻状态；
        // 若将来出现"同批死亡之后才改状态"的产出方，这里要按事件序补齐（独立复核 L-3）。
        var entry = context.State.Seat(death.Seat);
        if (entry is null
            || entry.DrunkValue is not { } drunk
            || entry.PoisonValue is not { } poison)
        {
            events.Add(new SweetheartDeathSkippedEvent
            {
                Sweetheart = death.Seat,
                Reason = "心上人死亡时醉酒 / 中毒维度未观测：判不了能力是否生效，不开裁定（D-0015；R-0039 第 5 条）",
            });
            return;
        }

        if (drunk != DrunkState.Sober || poison != PoisonState.Healthy)
        {
            events.Add(new SweetheartDeathSkippedEvent
            {
                Sweetheart = death.Seat,
                Reason = "心上人死亡时能力未生效（醉酒 / 中毒）：不开裁定、醉酒不施加（R-0039 第 5 条）",
            });
            return;
        }

        if (context.Seats.Count == 0)
        {
            // 座次名单为空属配置缺陷：不挂一个没人能答的裁定点。
            events.Add(new SweetheartDeathSkippedEvent
            {
                Sweetheart = death.Seat,
                Reason = "座次名单为空：列不出醉酒候选，裁定无法开（数据缺陷；显式跳过）",
            });
            return;
        }

        events.Add(new DecisionPointRaisedEvent
        {
            // 触发来源：不由任何槽位承载（白天死亡也立即处理），与呆瓜的触发型请求同族。
            SlotId = null,
            TriggerAbility = SweetheartAbility.DeathAbility,

            // 归属 = 死亡的心上人本人（触发型裁定没有槽位，圆环只能靠它归属）。
            AttributionSeat = death.Seat,
            DecisionPoint = new DecisionPoint
            {
                Id = SweetheartAbility.DecisionIdFor(death.Seat),
                Prompt = new ChoicePrompt
                {
                    Context = $"心上人（{death.Seat.Value} 号）死亡：请选择一名玩家开始醉酒"
                        + "（任一玩家，含已死亡；持续到心上人离场）"
                        + "（百科《心上人》· 2026-10-01 抓取 · 运作方式）",
                    Options =
                    [
                        .. context.Seats
                            .OrderBy(seat => seat.Value)
                            .Select(seat => new DecisionOption
                            {
                                Value = SeatChoice.Format(seat),
                                Preview = $"{seat.Value} 号玩家",
                            }),
                    ],
                    OnNoOption = NoOptionBehavior.BlockAndAlert,
                },
            },
        });
    }

    /// <summary>裁定结清：说书人选了目标 → 施加持续醉酒；未裁定（强推 / 收口）→ 显式跳过。</summary>
    private static void HandleResolved(
        EventTriggerContext context,
        DecisionPointResolvedEvent resolved,
        List<GameEvent> events)
    {
        if (context.Machine is null)
        {
            return;
        }

        var sweetheart = SweetheartOf(resolved.DecisionPointId, context.Seats);
        if (sweetheart is null || IsResolutionHandled(context, sweetheart.Value, events))
        {
            return;
        }

        if (resolved.Decision is null)
        {
            events.Add(new SweetheartDeathSkippedEvent
            {
                Sweetheart = sweetheart.Value,
                Reason = $"说书人没有裁定醉酒目标（{resolved.Note ?? "未说明"}）：醉酒不施加（强推 / 收口的显式记录）",
            });
            return;
        }

        var target = SeatChoice.Parse(resolved.Decision)
            ?? throw new InvalidOperationException($"心上人的醉酒目标不是合法席位编码：{resolved.Decision}");
        if (!context.Seats.Contains(target))
        {
            // 说书人裁定值不经过选项闸的精确匹配，内核必须自己拒绝越界编码（与理发师同族）。
            throw new InvalidOperationException(
                $"心上人的醉酒目标（{target.Value} 号）不在本局座次名单里：裁定编码无效");
        }

        events.Add(new PersistentEffectAppliedEvent
        {
            Effect = new PersistentEffect
            {
                Id = SweetheartAbility.DrunkEffectId(sweetheart.Value),
                Source = sweetheart.Value,
                Ability = SweetheartAbility.DeathAbility,
                Target = target,
                SourceCharacter = SweetheartAbility.Character,
                Dimension = EffectDimension.Drunk,
                // 效果在来源**死亡之后**落账（死亡事件先于它），来源失效传播不会回溯终止；
                // 生效判定也不看来源状态——只随来源换角（离场）终止（R-0039 第 4 条）。
                SourceStateIndependent = true,
            },
        });

        // 维度（目标醉酒）由结算对账按仍生效的效果产出（D-0015 推论 1），这里不直接写状态。
    }

    /// <summary>死亡处理是否已存在：跳过账 / 未了结裁定 / 效果账 / 本批已产出。</summary>
    private static bool IsDeathHandled(
        EventTriggerContext context,
        SeatId sweetheart,
        IReadOnlyList<GameEvent> produced)
    {
        if (IsResolutionHandled(context, sweetheart, produced))
        {
            return true;
        }

        if (context.Machine?.AwaitingDecision is { } decision
            && decision.Id == SweetheartAbility.DecisionIdFor(sweetheart))
        {
            return true;
        }

        return produced.OfType<DecisionPointRaisedEvent>()
            .Any(raised => raised.DecisionPoint.Id == SweetheartAbility.DecisionIdFor(sweetheart));
    }

    /// <summary>裁定 / 效果是否已落地：跳过账 / 效果账 / 本批已产出的跳过。</summary>
    private static bool IsResolutionHandled(
        EventTriggerContext context,
        SeatId sweetheart,
        IReadOnlyList<GameEvent> produced)
    {
        if (context.Machine?.SweetheartSkips.Any(record => record.Sweetheart == sweetheart) == true)
        {
            return true;
        }

        if (context.State.EffectsSourcedBy(sweetheart)
            .Any(effect => effect.Ability == SweetheartAbility.DeathAbility))
        {
            return true;
        }

        return produced.OfType<SweetheartDeathSkippedEvent>()
            .Any(skipped => skipped.Sweetheart == sweetheart);
    }

    /// <summary>是否已有任意未了结的心上人裁定（跨事件防覆盖：步骤机同时只挂一个裁定点）。</summary>
    private static bool HasPendingSweetheartDecision(
        EventTriggerContext context,
        IReadOnlyList<GameEvent> produced) =>
        (context.Machine is { AwaitingDecisionTriggerAbility: { } ability }
            && ability == SweetheartAbility.DeathAbility)
        || produced.OfType<DecisionPointRaisedEvent>()
            .Any(raised => raised.TriggerAbility == SweetheartAbility.DeathAbility);

    /// <summary>从裁定点标识反查是哪名心上人的触发（标识由本触发器派生，格式固定）。</summary>
    private static SeatId? SweetheartOf(DecisionPointId decisionPointId, IReadOnlyList<SeatId> seats)
    {
        foreach (var seat in seats)
        {
            if (SweetheartAbility.DecisionIdFor(seat) == decisionPointId)
            {
                return seat;
            }
        }

        return null;
    }
}
