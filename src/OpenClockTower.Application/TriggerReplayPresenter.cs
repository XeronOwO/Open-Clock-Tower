using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 角色触发窗口与整局事实步骤：疯狂要求 / 呆瓜选择 / 待定死亡 / 麻脸巫婆之夜 /
/// 理发师之夜 / 贤者展示 / 心上人 / 方古侵染（D-0020 步骤目录）。
/// </summary>
internal sealed class TriggerReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(MadnessRequirementIssuedEvent),
        typeof(MadnessRequirementTerminatedEvent),
        typeof(KlutzChoiceMadeEvent),
        typeof(KlutzChoiceSkippedEvent),
        typeof(DeferredDeathRecordedEvent),
        typeof(DeferredDeathResolvedEvent),
        typeof(PitHagNightOpenedEvent),
        typeof(PitHagNightClosedEvent),
        typeof(SageNightOpenedEvent),
        typeof(SageNightClosedEvent),
        typeof(SageNightSkippedEvent),
        typeof(BarberNightOpenedEvent),
        typeof(BarberNightClosedEvent),
        typeof(BarberNightSkippedEvent),
        typeof(SweetheartDeathSkippedEvent),
        typeof(FangGuInfectionRecordedEvent),
    ];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        MadnessRequirementIssuedEvent issued => PresentMadnessIssued(context, issued),
        MadnessRequirementTerminatedEvent terminated => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = "疯狂要求撤下",
            Detail = terminated.Termination.Reason,
        },
        KlutzChoiceMadeEvent choice => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(choice.Klutz)} 的呆瓜公开选择：{context.SeatText.Seat(choice.Target)}",
        },
        KlutzChoiceSkippedEvent klutzSkipped => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(klutzSkipped.Klutz)} 的呆瓜选择跳过",
            Detail = klutzSkipped.Reason,
        },
        DeferredDeathRecordedEvent recorded => PresentDeferredRecorded(context, recorded),
        DeferredDeathResolvedEvent resolved => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"待定死亡裁定：{context.SeatText.Seat(resolved.Target)} "
                + (resolved.Killed ? "死亡" : "存活"),
            Detail = resolved.Note,
        },
        PitHagNightOpenedEvent pitHagOpened => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(pitHagOpened.Source)} 创造恶魔：死亡裁量窗口开启"
                + $"（至第 {pitHagOpened.ClosesAfterSlotIndex} 个槽位）",
            Detail = $"窗口能力：{ReplayText.Ability(pitHagOpened.CasualtyAbility)}",
        },
        PitHagNightClosedEvent pitHagClosed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = "死亡裁量窗口关闭",
            Detail = pitHagClosed.Note,
        },
        SageNightOpenedEvent sageOpened => PresentSageOpened(context, sageOpened),
        SageNightClosedEvent sageClosed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = "贤者当夜展示结束",
            Detail = sageClosed.Note,
        },
        SageNightSkippedEvent sageSkipped => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(sageSkipped.Sage)} 的贤者触发跳过",
            Detail = sageSkipped.Reason,
        },
        BarberNightOpenedEvent barberOpened => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(barberOpened.Source)} 死亡：今晚理发待交互",
            Detail = barberOpened.Note,
        },
        BarberNightClosedEvent barberClosed => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = "今晚理发结清",
            Detail = barberClosed.Note,
        },
        BarberNightSkippedEvent barberSkipped => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(barberSkipped.Seat)} 的理发交互跳过",
            Detail = barberSkipped.Reason,
        },
        SweetheartDeathSkippedEvent sweetheartSkipped => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(sweetheartSkipped.Sweetheart)} 的心上人触发跳过",
            Detail = sweetheartSkipped.Reason,
        },
        FangGuInfectionRecordedEvent infection => new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"方古「限一次」已使用：{context.SeatText.Seat(infection.Source)} → "
                + $"{context.SeatText.Seat(infection.Seat)} 侵染",
        },
        _ => throw new InvalidOperationException(
            $"TriggerReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };

    private static ReplayStep PresentMadnessIssued(
        ReplayStepContext context,
        MadnessRequirementIssuedEvent issued)
    {
        var requirement = issued.Requirement;
        var details = new List<string>
        {
            $"来源 {context.SeatText.Seat(requirement.Source)}；能力 {ReplayText.Ability(requirement.Ability)}",
        };

        if (requirement.ExpiresAtDay is { } expiresAt)
        {
            details.Add($"第 {expiresAt} 天到期");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(requirement.Seat)} 被要求疯狂证明自己是「{requirement.ProveToBe}」",
            Detail = string.Join("；", details),
        };
    }

    private static ReplayStep PresentDeferredRecorded(
        ReplayStepContext context,
        DeferredDeathRecordedEvent recorded)
    {
        var details = new List<string> { recorded.Note };
        if (recorded.Transformation is { } transformation)
        {
            details.Add(
                $"确认后转化为 {ReplayText.CharacterValue(transformation.Character)}"
                + $"（{ReplayText.Alignment(transformation.Alignment)}），"
                + $"{context.SeatText.Seat(transformation.Dies)} 死亡");
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(recorded.Source)} 对 {context.SeatText.Seat(recorded.Target)} 的击杀进入待定死亡",
            Detail = string.Join("；", details),
        };
    }

    private static ReplayStep PresentSageOpened(ReplayStepContext context, SageNightOpenedEvent opened)
    {
        var details = new List<string>
        {
            $"击杀时恶魔角色：{ReplayText.CharacterValue(opened.DemonCharacter)}",
        };

        details.Add(opened.Effective switch
        {
            true => "能力生效",
            false => "能力未生效",
            null => "能力生效性未记录",
        });

        if (!string.IsNullOrEmpty(opened.Note))
        {
            details.Add(opened.Note);
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Trigger,
            Phase = context.Phase,
            Summary = $"{context.SeatText.Seat(opened.Sage)} 被恶魔 {context.SeatText.Seat(opened.Demon)} 击杀：当夜展示",
            Detail = string.Join("；", details),
        };
    }
}
