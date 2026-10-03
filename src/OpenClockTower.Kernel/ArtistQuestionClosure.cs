namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家提问的结清方式（<c>docs/standard/rulings.md</c> R-0040）。
/// </summary>
public enum ArtistQuestionClosure
{
    /// <summary>说书人给出「是 / 不是 / 我不知道」之一：记一次能力使用、落「失去能力」标记。</summary>
    Answered,

    /// <summary>说书人要求重问（问题无法用三种答案回答）：不记使用、不落标记，可在同一白天重新提问。</summary>
    Returned,

    /// <summary>被强推 / 收口越过：显式作废、不记使用（D-0014：每次越过都产出可审计事件）。</summary>
    Abandoned,
}
