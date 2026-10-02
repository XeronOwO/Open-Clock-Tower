using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 夜晚顺序表上的一条：黄昏 / 信息环节 / 某个角色的行动 / 黎明。
/// </summary>
/// <remarks>
/// <para>
/// **纯数据**：只回答「先后次序」。触发条件的求值、选项生成与效果结算属结算引擎与角色实现
/// （docs/architecture/current.md §2.6）；本表不含这些内容。
/// </para>
/// <para>
/// 顺序表**是提示，不是铁律**（术语表 night-order）：与角色能力描述冲突时，以能力描述为准。
/// </para>
/// </remarks>
public sealed record NightOrderEntry
{
    /// <summary>条目种类。</summary>
    public required NightOrderEntryKind Kind { get; init; }

    /// <summary>角色条目对应的角色；非角色条目为 null。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>构造一个角色行动条目。</summary>
    public static NightOrderEntry Action(CharacterId character) =>
        new() { Kind = NightOrderEntryKind.CharacterAction, Character = character };

    /// <summary>
    /// 构造一个角色触发格条目（如理发师）：进入时只标记时机，
    /// 是否开操作请求由触发管线按步骤机事实决定（不进建表的行动契约闸）。
    /// </summary>
    public static NightOrderEntry Trigger(CharacterId character) =>
        new() { Kind = NightOrderEntryKind.CharacterTrigger, Character = character };

    /// <summary>构造一个非角色条目（黄昏 / 信息环节 / 黎明）。</summary>
    public static NightOrderEntry Step(NightOrderEntryKind kind) =>
        kind is NightOrderEntryKind.CharacterAction or NightOrderEntryKind.CharacterTrigger
            ? throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "角色条目必须用 Action(character) / Trigger(character) 构造：没有角色信息的角色条目是坏数据")
            : new NightOrderEntry { Kind = kind };
}
