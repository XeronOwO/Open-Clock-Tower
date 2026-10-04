namespace OpenClockTower.Contracts;

/// <summary>配板建议（说书人查询；瞬态、不落账、不进事件流）。</summary>
public sealed record SetupProposalDto
{
    /// <summary>是否给出建议；false 时看 FailureCode / FailureMessage。</summary>
    public required bool Ok { get; init; }

    /// <summary>本次使用的显式随机输入（种子）：重摇 = 新种子；复现 = 原样传回。</summary>
    public required string Seed { get; init; }

    /// <summary>本次配板覆盖的非旅行者人数（R-0046：分布表按它取行）。</summary>
    public required int NonTravellerCount { get; init; }

    /// <summary>本局旅行者人数（席位总数 − 非旅行者人数）：不参与配板、不占四类型名额（R-0046）。</summary>
    public required int TravellerCount { get; init; }

    /// <summary>席位 → 角色（按席位升序）。</summary>
    public required IReadOnlyList<SeatCharacterAssignmentDto> Assignments { get; init; }

    /// <summary>净分布。</summary>
    public required IReadOnlyList<SetupTypeCountDto> Distribution { get; init; }

    /// <summary>设置调整与钳制的显式说明。</summary>
    public required IReadOnlyList<string> Notes { get; init; }

    /// <summary>失败码（`setup.*`）；成功为 null。</summary>
    public string? FailureCode { get; init; }

    /// <summary>失败说明（中文）。</summary>
    public string? FailureMessage { get; init; }
}
