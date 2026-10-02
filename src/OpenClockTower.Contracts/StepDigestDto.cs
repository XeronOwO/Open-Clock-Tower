namespace OpenClockTower.Contracts;

/// <summary>
/// 每步摘要（票据「说书人上帝视角」第 3 条）：当前槽位的行动者、他身上全部已观测状态及来源、
/// 本步能力判定与「无合法选项时的行为」。
/// </summary>
/// <remarks>
/// 由服务端算好：能力判定优先取本槽位的已结算结论，未结算时给按当前账的预览
/// （<see cref="SlotAbilityDto.Basis"/> 标明依据）。前端只呈现，不做规则推断（D-0018）。
/// </remarks>
public sealed record StepDigestDto
{
    /// <summary>本槽位行动者席位。</summary>
    public required int Seat { get; init; }

    /// <summary>行动者的角色（建表时写入槽位的角色）。</summary>
    public string? Character { get; init; }

    /// <summary>行动者的状态账行：全部已观测维度及逐维度归因（未观测的维度不出现）。</summary>
    public SeatStateDto? State { get; init; }

    /// <summary>本步能力判定：已结算结论优先；未结算时是按当前账的预览。</summary>
    public SlotAbilityDto? Ability { get; init; }

    /// <summary>本槽位交给玩家的合法选项数量。</summary>
    public int? OptionCount { get; init; }

    /// <summary>无合法选项时的行为（Kernel NoOptionBehavior，R-0009）。</summary>
    public string? OnNoOption { get; init; }
}
