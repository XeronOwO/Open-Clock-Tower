namespace OpenClockTower.Kernel;

/// <summary>
/// 规则层对一条艺术家裁决的结清结论（内核按它记账、清问题、下发信息）。
/// </summary>
/// <remarks>
/// 口径见 <c>docs/standard/rulings.md</c> R-0040：回答（是 / 不是 / 我不知道）记一次使用
/// （含未生效——三-3「使用机会被浪费」）；「要求重问」不记使用、不落标记。
/// </remarks>
public sealed record ArtistQuestionResolution
{
    /// <summary>结清方式。</summary>
    public required ArtistQuestionRuling Ruling { get; init; }

    /// <summary>回答是否正常生效（<see cref="ArtistQuestionRuling.Answered"/> 时有意义）。</summary>
    public bool Effective { get; init; }

    /// <summary>失效分类（R-0004；如涡流叠加）；正常生效且无外部干扰时为空。</summary>
    public IReadOnlyList<MalfunctionKind> Malfunctions { get; init; } = [];

    /// <summary>说明（进记账事件与信息事件的 Note）。</summary>
    public string? Note { get; init; }

    /// <summary>结清产出的后续事件（信息结果等）；要求重问时为空。</summary>
    public IReadOnlyList<GameEvent> Events { get; init; } = [];
}
