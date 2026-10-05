namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者提问的结清裁决：给出两条信息，或裁定文本不合格式（显式拒绝）。
/// </summary>
/// <remarks>
/// 与 <see cref="ArtistQuestionRuling"/> 的差别：博学者的裁定是**自由文本**（两条信息由说书人写），
/// 因此多出一种"输入不合法"的结论——它必须变成一条可读的拒绝，而不是抛异常
/// （口径见 <c>docs/standard/rulings.md</c> R-0057）。
/// </remarks>
public enum SavantQuestionRuling
{
    /// <summary>说书人给出了两条信息。</summary>
    Answered,

    /// <summary>裁定文本不合格式（例如没有用 <c>|</c> 分成两条）：显式拒绝，不记账、不下发。</summary>
    Invalid,
}
