namespace OpenClockTower.Kernel;

/// <summary>
/// 规则层对一条博学者裁定的结清结论（内核按它记账、清提问、下发信息）。
/// </summary>
/// <remarks>
/// 口径见 <c>docs/standard/rulings.md</c> R-0057：说书人给两条信息（用 <c>|</c> 分隔），
/// 平台**不判定哪条为真**（D-0002）——两条都标「可能为假」；
/// <see cref="SavantQuestionRuling.Invalid"/> 用于裁定文本不合格式（自由文本是用户输入，显式拒绝而不抛异常）。
/// </remarks>
public sealed record SavantQuestionResolution
{
    /// <summary>结清方式。</summary>
    public required SavantQuestionRuling Ruling { get; init; }

    /// <summary>回答是否正常生效（<see cref="SavantQuestionRuling.Answered"/> 时有意义）。</summary>
    public bool Effective { get; init; }

    /// <summary>失效分类（R-0004；如涡流叠加）；正常生效且无外部干扰时为空。</summary>
    public IReadOnlyList<MalfunctionKind> Malfunctions { get; init; } = [];

    /// <summary>说明（进记账事件与信息事件的 Note）。</summary>
    public string? Note { get; init; }

    /// <summary>结清产出的后续事件（两条信息结果）；<see cref="SavantQuestionRuling.Invalid"/> 时为空。</summary>
    public IReadOnlyList<GameEvent> Events { get; init; } = [];
}
