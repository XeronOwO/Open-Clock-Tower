namespace OpenClockTower.Kernel;

/// <summary>
/// 艺术家裁决的结清方式（契约可返回的两态；强推作废由内核直接使用
/// <see cref="ArtistQuestionClosure.Abandoned"/>，不经过契约）。
/// </summary>
public enum ArtistQuestionRuling
{
    /// <summary>说书人给出「是 / 不是 / 我不知道」之一：记一次使用并下发信息。</summary>
    Answered,

    /// <summary>说书人要求重问：不记使用、不落标记，可在同一白天重新提问。</summary>
    Returned,
}
