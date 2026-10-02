namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤表上的一个槽位：一次角色行动、一个空槽位、一个节拍环节，或黎明等待。
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

    /// <summary>
    /// 行动者的角色 slug（建表时写入）；结算时按它从注入目录取结算契约。
    /// </summary>
    /// <remarks>
    /// 这里刻意只存**数据**不存行为对象：计划随 <see cref="PhaseStartedEvent"/> 进事件流、
    /// 步骤机状态进 JSON 快照，接口类型的成员过不了序列化往返。
    /// </remarks>
    public CharacterId? Owner { get; init; }

    /// <summary>
    /// 本槽位**对应**的角色（角色条目槽位一律有值；节拍 / 黎明 / 白天窗口为 null）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Owner"/> 的分工：Owner 是"建表时就在场的行动者"（空槽位没有）；
    /// Character 是"这个槽位是谁的位置"。角色变更能力（麻脸巫婆等）在夜里把某个角色创造出来之后，
    /// 尚未进入的槽位据此被**激活**（<see cref="SlotActivatedEvent"/>）——见
    /// <c>docs/standard/rulings.md</c> R-0030 第 6 条与百科《夜晚行动顺序一览》麻脸巫婆条
    /// 「否则，就需要唤醒这名玩家」。
    /// </remarks>
    public CharacterId? Character { get; init; }

    /// <summary>座位依赖：任一不满足即自动作废该请求。</summary>
    public IReadOnlyList<SeatDependency> Dependencies { get; init; } = [];

    /// <summary>构造一个角色行动槽位。</summary>
    /// <param name="id">槽位标识。</param>
    /// <param name="actor">行动者席位。</param>
    /// <param name="prompt">选择契约。</param>
    /// <param name="dependencies">座位依赖。</param>
    /// <param name="owner">行动者角色（结算契约的检索键）。</param>
    public static StepSlot Action(
        StepSlotId id,
        SeatId actor,
        ChoicePrompt prompt,
        IReadOnlyList<SeatDependency>? dependencies = null,
        CharacterId? owner = null) =>
        new()
        {
            Id = id,
            Kind = StepSlotKind.Action,
            Actor = actor,
            Prompt = prompt,
            Dependencies = dependencies ?? [],
            Owner = owner,
            Character = owner,
        };

    /// <summary>
    /// 构造一个「代行能力」槽位：能力属于 <paramref name="owner"/>（结算契约的检索键），
    /// 但行动者是 <paramref name="actor"/>——<see cref="Character"/> 记**行动者本人**的角色。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Action"/> 的差别只在 <see cref="Owner"/> 与 <see cref="Character"/> 的分工：
    /// 前者是"用哪个角色的契约结算"，后者是"这一格由谁行动、进入时按什么确认他还站得住"。
    /// 哲学家获得他人能力后由他代行时，二者不再重合（口径见 <c>docs/standard/rulings.md</c> R-0036）：
    /// 槽位仍是被获得角色的位置，行动者却是哲学家。
    /// </remarks>
    /// <param name="id">槽位标识（被获得角色的位置）。</param>
    /// <param name="actor">行动者席位（能力的获得者）。</param>
    /// <param name="actorCharacter">行动者本人的角色（进入这一格时的确认依据）。</param>
    /// <param name="prompt">选择契约（被获得角色的提示）。</param>
    /// <param name="dependencies">座位依赖。</param>
    /// <param name="owner">能力所属的角色（结算契约的检索键）。</param>
    public static StepSlot GrantedAction(
        StepSlotId id,
        SeatId actor,
        CharacterId actorCharacter,
        ChoicePrompt prompt,
        IReadOnlyList<SeatDependency> dependencies,
        CharacterId owner) =>
        new()
        {
            Id = id,
            Kind = StepSlotKind.Action,
            Actor = actor,
            Prompt = prompt,
            Dependencies = dependencies,
            Owner = owner,
            Character = actorCharacter,
        };

    /// <summary>构造一个空槽位（角色不在场 / 已死亡 / 被跳过）。</summary>
    /// <param name="id">槽位标识。</param>
    /// <param name="character">这个槽位对应的角色；null = 非角色条目。</param>
    public static StepSlot Empty(StepSlotId id, CharacterId? character = null) =>
        new() { Id = id, Kind = StepSlotKind.Empty, Character = character };

    /// <summary>
    /// 构造一个触发槽位（如理发师格）：进入时只标记「时机到了」，
    /// 是否开操作请求由触发管线按步骤机事实决定（<see cref="BarberNight"/>）。
    /// </summary>
    /// <param name="id">槽位标识。</param>
    /// <param name="character">这个槽位对应的角色（触发格必须有角色归属）。</param>
    public static StepSlot Trigger(StepSlotId id, CharacterId character) =>
        new() { Id = id, Kind = StepSlotKind.Trigger, Character = character };

    /// <summary>构造一个黎明等待槽位。</summary>
    public static StepSlot DawnWait(StepSlotId id) => new() { Id = id, Kind = StepSlotKind.DawnWait };

    /// <summary>构造一个节拍槽位（黄昏 / 信息环节等非角色行动条目）。</summary>
    public static StepSlot Beat(StepSlotId id) => new() { Id = id, Kind = StepSlotKind.Beat };

    /// <summary>构造白天窗口槽位：白天阶段的唯一槽位，不消耗配额、不自动推进。</summary>
    public static StepSlot DayWindow(StepSlotId id) => new() { Id = id, Kind = StepSlotKind.DayWindow };
}
