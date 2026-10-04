namespace OpenClockTower.Kernel;

/// <summary>
/// 按仍生效的效果重算由效果压制的维度，产出解除 / 重挂 / 换链接的状态变化事件（D-0015 推论 1）。
/// </summary>
/// <remarks>
/// <para>
/// 状态账自己不做这件事：折叠只记事实（D-0015 选 A）。但如果没人做，来源死亡后账本会停在
/// 「效果已终止、人还中毒」——既错、又回答不了说书人的"为什么"。本类就是那条义务的落点：
/// 读持续型效果的终止 / 挂起状态，对中毒与醉酒两个维度各求一次"当前应为值"。
/// </para>
/// <para>
/// **不猜**：来源维度没观测齐、效果是否生效判定不了时，这一维**什么都不做**（保持现状），
/// 而不是按"大概没生效"解除掉；说书人上报的、没有效果链接的中毒（<see cref="StateFact{T}.EffectId"/>
/// 为 null）也不归这里管——引擎不撤销没有归因的事实。
/// </para>
/// </remarks>
public static class DimensionEffectReconciler
{
    /// <summary>对每个席位的中毒 / 醉酒两个维度求应为值，产出必要的变化事件。</summary>
    public static IReadOnlyList<GameEvent> Reconcile(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var events = new List<GameEvent>();
        foreach (var entry in state.Seats)
        {
            ReconcilePoison(state, entry, events);
            ReconcileDrunk(state, entry, events);
        }

        return events;
    }

    private static void ReconcilePoison(GameState state, SeatStateEntry entry, List<GameEvent> events)
    {
        var (determined, primary) = Support(state, entry.Seat, EffectDimension.Poison);
        if (!determined)
        {
            return;
        }

        var current = entry.Poison;
        if (primary is not null)
        {
            if (current is { Value: PoisonState.Poisoned } fact && fact.EffectId == primary.Id)
            {
                return;
            }

            events.Add(new SeatStateChangedEvent
            {
                Seat = entry.Seat,
                Poison = PoisonState.Poisoned,
                Reason = current is { Value: PoisonState.Poisoned }
                    ? $"持续型效果 {primary.Id} 生效：中毒（链接由 {current.EffectId?.Value ?? "无"} 迁移而来）"
                    : $"持续型效果 {primary.Id} 生效：中毒",
                CausedBy = primary.Source,
                EffectId = primary.Id,
            });
            return;
        }

        // 当前的中毒由某条效果支撑、而它已终止 / 挂起：由引擎解除（D-0015：账本只报终止，翻转维度是引擎义务）。
        if (current is not { Value: PoisonState.Poisoned } || current.EffectId is not { } releasedEffectId)
        {
            return;
        }

        events.Add(new SeatStateChangedEvent
        {
            Seat = entry.Seat,
            Poison = PoisonState.Healthy,
            Reason = ReleaseReason(state, entry.Seat, "中毒", releasedEffectId),
            CausedBy = current.CausedBy,
            EffectId = releasedEffectId,
        });
    }

    private static void ReconcileDrunk(GameState state, SeatStateEntry entry, List<GameEvent> events)
    {
        var (determined, primary) = Support(state, entry.Seat, EffectDimension.Drunk);
        if (!determined)
        {
            return;
        }

        var current = entry.Drunk;
        if (primary is not null)
        {
            if (current is { Value: DrunkState.Drunk } fact && fact.EffectId == primary.Id)
            {
                return;
            }

            events.Add(new SeatStateChangedEvent
            {
                Seat = entry.Seat,
                Drunk = DrunkState.Drunk,
                Reason = current is { Value: DrunkState.Drunk }
                    ? $"持续型效果 {primary.Id} 生效：醉酒（链接由 {current.EffectId?.Value ?? "无"} 迁移而来）"
                    : $"持续型效果 {primary.Id} 生效：醉酒",
                CausedBy = primary.Source,
                EffectId = primary.Id,
            });
            return;
        }

        if (current is not { Value: DrunkState.Drunk } || current.EffectId is not { } releasedEffectId)
        {
            return;
        }

        events.Add(new SeatStateChangedEvent
        {
            Seat = entry.Seat,
            Drunk = DrunkState.Sober,
            Reason = ReleaseReason(state, entry.Seat, "醉酒", releasedEffectId),
            CausedBy = current.CausedBy,
            EffectId = releasedEffectId,
        });
    }

    /// <summary>
    /// 解除维度时的说明：区分「效果终止 / 挂起」与「目标处于咖啡师「清醒且健康」窗口」两种原因——
    /// 后者不是效果失效，而是标记照记、暂不生效（R-0047 第 1–3 条），说书人视图要能看出是哪种。
    /// </summary>
    private static string ReleaseReason(GameState state, SeatId seat, string dimension, EffectId effectId) =>
        state.WindowOn(seat, EffectWindowKind.AfflictionImmunity) == true
            ? $"咖啡师「清醒且健康」窗口生效（R-0047 第 1 条）：{dimension}标记照记、暂不生效"
            : $"持续型效果 {effectId} 已终止或挂起：{dimension}解除";

    /// <summary>
    /// 求这一维当前应由哪条效果支撑：
    /// <c>Determined=false</c> = 有支持效果但生效与否判定不了（不猜、不动）；
    /// <c>Primary=null</c> = 确定没有生效的支撑效果；
    /// 否则返回第一条确认生效的支持效果（账内顺序确定，D-0008）。
    /// </summary>
    private static (bool Determined, PersistentEffect? Primary) Support(
        GameState state,
        SeatId seat,
        EffectDimension dimension)
    {
        PersistentEffect? primary = null;
        var unknown = false;

        foreach (var effect in state.LiveEffectsOn(seat))
        {
            if (effect.Dimension != dimension)
            {
                continue;
            }

            switch (state.IsOperative(effect))
            {
                case true when primary is null:
                    primary = effect;
                    break;
                case null:
                    unknown = true;
                    break;
                default:
                    break;
            }
        }

        if (primary is not null)
        {
            return (true, primary);
        }

        if (unknown)
        {
            return (false, null);
        }

        return (true, null);
    }
}
