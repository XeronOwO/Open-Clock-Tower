namespace OpenClockTower.Kernel;

/// <summary>
/// 呆瓜选择的账目：选了什么（<see cref="Target"/> 非空）或为什么没选（<see cref="Target"/> 为空）。
/// </summary>
/// <remarks>
/// 折进 <see cref="StepMachineState.KlutzChoices"/>：触发器的幂等依据——有了这一条，
/// 后续黎明不会为同一名呆瓜重复开选择（R-0027）。
/// </remarks>
public sealed record KlutzChoiceRecord
{
    /// <summary>呆瓜席位（选择者）。</summary>
    public required SeatId Klutz { get; init; }

    /// <summary>选中的席位；null = 这次没有做出选择（跳过）。</summary>
    public SeatId? Target { get; init; }

    /// <summary>说明（选择结果或跳过原因，人类可读，进审计与说书人视图）。</summary>
    public required string Detail { get; init; }

    /// <summary>是否真的做出了选择。</summary>
    public bool IsMade => Target is not null;
}
