using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Application;

/// <summary>
/// 状态与效果步骤：六维度状态变化（含恶魔击杀红箭头 / 死亡帷幕 / 换角 / 中毒 / 醉酒标记）
/// 与效果生命周期（D-0020 步骤目录）。
/// </summary>
internal sealed class StateReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(SeatStateChangedEvent),
        typeof(PersistentEffectAppliedEvent),
        typeof(PersistentEffectTerminatedEvent),
        typeof(InstantaneousEffectAppliedEvent),
    ];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        SeatStateChangedEvent changed => PresentChanged(context, changed),
        PersistentEffectAppliedEvent applied => PresentEffectApplied(context, applied),
        PersistentEffectTerminatedEvent terminated => PresentEffectTerminated(context, terminated),
        InstantaneousEffectAppliedEvent instantaneous => PresentInstantaneous(context, instantaneous),
        _ => throw new InvalidOperationException(
            $"StateReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };

    private static ReplayStep PresentChanged(ReplayStepContext context, SeatStateChangedEvent changed)
    {
        var summaryParts = new List<string>();
        if (changed.Life is { } life)
        {
            summaryParts.Add(life == LifeState.Alive ? "存活" : "死亡");
        }

        if (changed.Character is { } character)
        {
            summaryParts.Add(changed.PreviousCharacter is { } previous && previous != character
                ? $"换角：{ReplayText.CharacterValue(previous)} → {ReplayText.CharacterValue(character)}"
                : $"角色：{ReplayText.CharacterValue(character)}");
        }

        if (changed.Alignment is { } alignment)
        {
            summaryParts.Add($"阵营：{ReplayText.Alignment(alignment)}");
        }

        if (changed.Drunk is { } drunk)
        {
            summaryParts.Add(drunk == DrunkState.Drunk ? "醉酒" : "清醒");
        }

        if (changed.Poison is { } poison)
        {
            summaryParts.Add(poison == PoisonState.Poisoned ? "中毒" : "健康");
        }

        var details = new List<string> { changed.Reason };
        if (changed.CausedBy is { } causedBy)
        {
            details.Add($"归因：{ReplayText.Seat(causedBy)}");
        }

        var markers = new List<ReplayMarker>();
        if (changed.Life == LifeState.Dead)
        {
            markers.Add(new ReplayMarker { Kind = "shroud", Seat = changed.Seat });
            if (IsDemonKill(context, changed, out var killer))
            {
                markers.Add(new ReplayMarker
                {
                    Kind = "kill-arrow",
                    From = killer,
                    To = changed.Seat,
                });
            }
        }

        if (changed.Character is { } newCharacter
            && changed.PreviousCharacter is { } oldCharacter
            && oldCharacter != newCharacter)
        {
            markers.Add(new ReplayMarker
            {
                Kind = "character-change",
                Seat = changed.Seat,
                Text = $"{ReplayText.CharacterValue(oldCharacter)} → {ReplayText.CharacterValue(newCharacter)}",
            });
        }

        if (changed.Poison == PoisonState.Poisoned)
        {
            markers.Add(new ReplayMarker { Kind = "poisoned", Seat = changed.Seat });
        }

        if (changed.Drunk == DrunkState.Drunk)
        {
            markers.Add(new ReplayMarker { Kind = "drunk", Seat = changed.Seat });
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.State,
            Phase = context.Phase,
            Summary = $"{ReplayText.Seat(changed.Seat)}：{string.Join("；", summaryParts)}",
            Detail = string.Join("；", details),
            Seats =
            [
                new ReplaySeatDelta
                {
                    Seat = changed.Seat,
                    Life = changed.Life,
                    Character = changed.Character,
                    PreviousCharacter = changed.PreviousCharacter,
                    Alignment = changed.Alignment,
                    Drunk = changed.Drunk,
                    Poison = changed.Poison,
                    Reason = changed.Reason,
                    CausedBy = changed.CausedBy,
                },
            ],
            Markers = markers,
        };
    }

    /// <summary>
    /// 「被恶魔击杀」判据（与 R-0038 同源）：死亡事件的导致方在**死亡时刻**的角色是恶魔。
    /// 角色未观测一律不画箭头（不猜，D-0015）；处决 / 女巫诅咒 / 说书人追加死亡不画；
    /// 自指归因（方古侵染时原方古自死，<c>CausedBy</c> = 自己）也不画——那不是"被击杀"。
    /// </summary>
    private static bool IsDemonKill(
        ReplayStepContext context,
        SeatStateChangedEvent changed,
        out SeatId? killer)
    {
        killer = changed.CausedBy;
        if (changed.CausedBy is not { } causedBy)
        {
            return false;
        }

        // 自指归因不是"被恶魔击杀"：画箭头会把"恶魔离场"读成"恶魔刀了自己"，与 R-0038 的语义不符。
        if (causedBy == changed.Seat)
        {
            return false;
        }

        var character = context.StateBefore.Seat(causedBy)?.CharacterValue;
        return character is { } value && SectsAndVioletsRoster.TypeOf(value) == CharacterType.Demon;
    }

    private static ReplayStep PresentEffectApplied(
        ReplayStepContext context,
        PersistentEffectAppliedEvent applied)
    {
        var effect = applied.Effect;
        var details = new List<string>
        {
            $"来源 {ReplayText.Seat(effect.Source)}（{ReplayText.CharacterValue(effect.SourceCharacter)}）",
        };

        if (effect.Dimension is { } dimension)
        {
            details.Add($"压制维度：{DimensionText(dimension)}");
        }

        if (effect.GrantedCharacter is { } granted)
        {
            details.Add($"获得角色：{ReplayText.CharacterValue(granted)}");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Effect,
            Phase = context.Phase,
            Summary = $"效果施加：{ReplayText.Ability(effect.Ability)} → {ReplayText.Seat(effect.Target)}",
            Detail = string.Join("；", details),
        };
    }

    private static ReplayStep PresentEffectTerminated(
        ReplayStepContext context,
        PersistentEffectTerminatedEvent terminated)
    {
        var effect = context.StateBefore.PersistentEffects
            .FirstOrDefault(item => item.Id == terminated.EffectId);

        var details = new List<string> { terminated.Termination.Reason };
        if (terminated.Termination.CausedBy is { } causedBy)
        {
            details.Add($"归因：{ReplayText.Seat(causedBy)}");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Effect,
            Phase = context.Phase,
            Summary = effect is null
                ? "效果终止"
                : $"效果终止：{ReplayText.Ability(effect.Ability)} → {ReplayText.Seat(effect.Target)}",
            Detail = string.Join("；", details),
        };
    }

    private static ReplayStep PresentInstantaneous(
        ReplayStepContext context,
        InstantaneousEffectAppliedEvent instantaneous)
    {
        var effect = instantaneous.Effect;
        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Effect,
            Phase = context.Phase,
            Summary = $"即时效果：{ReplayText.Ability(effect.Ability)} → {ReplayText.Seat(effect.Target)}",
            Detail = $"来源 {ReplayText.Seat(effect.Source)}",
        };
    }

    private static string DimensionText(EffectDimension dimension) => dimension switch
    {
        EffectDimension.Poison => "中毒",
        EffectDimension.Drunk => "醉酒",
        _ => dimension.ToString(),
    };
}
