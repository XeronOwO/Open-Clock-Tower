namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤表上的一个槽位：一次角色行动、一个空槽位，或黎明等待。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0013 §1：步骤表按**剧本的完整夜晚顺序表**展开，不按在场角色展开——
/// 角色不在场 / 已死亡 / 被跳过一律生成 <see cref="StepSlotKind.Empty"/> 槽位，**照样走完配额**。
/// </para>
/// <para>
/// <see cref="Prompt"/> 是给该玩家（仅 Action 槽位）的选择契约；<see cref="Dependencies"/>
/// 声明"哪些座位事实一旦变化，这个请求就失去意义"（自动作废依据，见 StepMachine）。
/// 计划构造方必须把行动者自身的关键事实（如存活、当前角色）也声明进来。
/// </para>
/// </remarks>
public sealed record StepSlot
{
    /// <summary>槽位稳定标识（同一计划内唯一）。</summary>
    public required StepSlotId Id { get; init; }

    /// <summary>槽位类型。</summary>
    public required StepSlotKind Kind { get; init; }

    /// <summary>行动者；仅 Action 槽位非空。</summary>
    public SeatId? Actor { get; init; }

    /// <summary>给行动者的选择契约；仅 Action 槽位需要。</summary>
    public ChoicePrompt? Prompt { get; init; }

    /// <summary>座位依赖：任一不满足即自动作废该请求。</summary>
    public IReadOnlyList<SeatDependency> Dependencies { get; init; } = [];

    /// <summary>构造一个角色行动槽位。</summary>
    public static StepSlot Action(
        StepSlotId id,
        SeatId actor,
        ChoicePrompt prompt,
        IReadOnlyList<SeatDependency>? dependencies = null) =>
        new()
        {
            Id = id,
            Kind = StepSlotKind.Action,
            Actor = actor,
            Prompt = prompt,
            Dependencies = dependencies ?? [],
        };

    /// <summary>构造一个空槽位（角色不在场 / 已死亡 / 被跳过）。</summary>
    public static StepSlot Empty(StepSlotId id) => new() { Id = id, Kind = StepSlotKind.Empty };

    /// <summary>构造一个黎明等待槽位。</summary>
    public static StepSlot DawnWait(StepSlotId id) => new() { Id = id, Kind = StepSlotKind.DawnWait };
}
