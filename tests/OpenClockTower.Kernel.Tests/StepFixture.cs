using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>构造测试用步骤表与选择契约的共享助手。</summary>
internal static class StepFixture
{
    /// <summary>构造一个带若干合法选项的选择契约。</summary>
    internal static ChoicePrompt Prompt(params string[] optionValues) => new()
    {
        Context = "测试用选择",
        Options = [.. optionValues.Select(value => new DecisionOption { Value = value, Preview = $"预览：{value}" })],
        OnNoOption = NoOptionBehavior.Skip,
    };

    /// <summary>构造一个无合法选项、按声明行为处理的选择契约。</summary>
    internal static ChoicePrompt EmptyPrompt(NoOptionBehavior behavior = NoOptionBehavior.Skip) => new()
    {
        Context = "没有合法选项的测试用选择",
        Options = [],
        OnNoOption = behavior,
    };

    /// <summary>构造一个阶段计划。</summary>
    internal static StepPlan Plan(string label, params StepSlot[] slots) => new()
    {
        Label = label,
        Phase = GamePhase.FirstNight,
        Slots = slots,
    };

    /// <summary>构造一个行动槽位；<paramref name="owner"/> 是结算契约的检索键。</summary>
    internal static StepSlot Action(
        string id,
        int seat,
        ChoicePrompt? prompt = null,
        IReadOnlyList<SeatDependency>? dependencies = null,
        string? owner = null) =>
        StepSlot.Action(
            new StepSlotId(id),
            new SeatId(seat),
            prompt ?? Prompt("option-a", "option-b"),
            dependencies,
            owner is null ? null : new CharacterId(owner));

    /// <summary>构造一个空槽位。</summary>
    internal static StepSlot Empty(string id) => StepSlot.Empty(new StepSlotId(id));

    /// <summary>构造一个"要求该座位存活"的依赖（声明行动者自身事实时使用）。</summary>
    internal static SeatDependency Alive(SeatId seat) => new() { Seat = seat, RequiredLife = LifeState.Alive };

    /// <summary>构造一个节拍槽位。</summary>
    internal static StepSlot Beat(string id) => StepSlot.Beat(new StepSlotId(id));

    /// <summary>构造一个黎明等待槽位。</summary>
    internal static StepSlot DawnWait(string id) => StepSlot.DawnWait(new StepSlotId(id));
}
