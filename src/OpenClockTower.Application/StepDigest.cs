using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 每步摘要（票据「说书人上帝视角」第 3 条）：当前槽位的行动者、他身上全部已观测状态及来源、
/// 本步能力判定与「无合法选项时的行为」。
/// </summary>
public sealed record StepDigest
{
    /// <summary>本槽位行动者席位。</summary>
    public required SeatId Seat { get; init; }

    /// <summary>行动者的角色（建表时写入槽位的角色）。</summary>
    public CharacterId? Character { get; init; }

    /// <summary>行动者的状态账行（全部已观测维度及逐维度归因）；还没观测过为 null。</summary>
    public SeatStateEntry? State { get; init; }

    /// <summary>本步能力判定：已结算结论优先，未结算时为按当前账的预览。</summary>
    public SlotAbilitySnapshot? Ability { get; init; }

    /// <summary>本槽位交给玩家的合法选项数量。</summary>
    public int? OptionCount { get; init; }

    /// <summary>无合法选项时的行为（R-0009）。</summary>
    public NoOptionBehavior? OnNoOption { get; init; }
}
