using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>能力结算与信息结果步骤（D-0020 步骤目录）。</summary>
internal sealed class AbilityReplayPresenter : IReplayStepPresenter
{
    /// <inheritdoc />
    public IReadOnlyList<Type> HandledTypes =>
    [
        typeof(AbilityResolvedEvent),
        typeof(InformationResultIssuedEvent),
    ];

    /// <inheritdoc />
    public ReplayStep Present(ReplayStepContext context) => context.Stored.Event switch
    {
        AbilityResolvedEvent resolved => PresentResolved(context, resolved),
        InformationResultIssuedEvent information => PresentInformation(context, information),
        _ => throw new InvalidOperationException(
            $"AbilityReplayPresenter 不认领事件 {context.Stored.Event.GetType().Name}"),
    };

    private static ReplayStep PresentResolved(ReplayStepContext context, AbilityResolvedEvent resolved)
    {
        var details = new List<string>
        {
            resolved.Effective ? "正常生效" : "未正常生效",
        };

        if (resolved.Malfunctions.Count > 0)
        {
            details.Add($"原因：{ReplayText.Malfunctions(resolved.Malfunctions)}");
        }

        if (!string.IsNullOrEmpty(resolved.Note))
        {
            details.Add(resolved.Note);
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Ability,
            Phase = context.Phase,
            Summary = $"{ReplayText.Seat(resolved.Actor)} 的能力结算：{ReplayText.Ability(resolved.Ability)}",
            Detail = string.Join("；", details),
        };
    }

    private static ReplayStep PresentInformation(
        ReplayStepContext context,
        InformationResultIssuedEvent information)
    {
        var details = new List<string>
        {
            $"能力 {ReplayText.Ability(information.Ability)}",
        };

        if (information.MayBeFalse)
        {
            details.Add("可能为假");
        }

        if (!string.IsNullOrEmpty(information.Note))
        {
            details.Add(information.Note);
        }

        return new ReplayStep
        {
            Sequence = context.Stored.Sequence,
            Kind = ReplayStepKind.Information,
            Phase = context.Phase,
            Summary = $"信息结果 → {ReplayText.Seat(information.Recipient)}：{information.Content}",
            Detail = string.Join("；", details),
        };
    }
}
