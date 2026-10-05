namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者提问的结清方式：说书人给出两条信息，或强推越过时作废。
/// </summary>
/// <remarks>
/// 与 <see cref="ArtistQuestionClosure"/> 的差别只有一处：博学者没有「要求重问」
/// （内容由说书人给，没有"问不出来"这回事）。口径见 <c>docs/standard/rulings.md</c> R-0057。
/// </remarks>
public enum SavantQuestionClosure
{
    /// <summary>说书人给出了两条信息（能力未生效时也给，可能两条都真或都假）。</summary>
    Answered,

    /// <summary>强推越过时作废：不记账、不产信息，绝不留到下一阶段。</summary>
    Abandoned,
}
