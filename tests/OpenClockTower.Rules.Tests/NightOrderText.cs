namespace OpenClockTower.Rules.Tests;

/// <summary>把顺序表条目序列转成便于断言的文本（失败时逐项可读）。</summary>
internal static class NightOrderText
{
    /// <summary>例如 <c>CharacterAction:philosopher</c> 或 <c>MinionInfo</c>。</summary>
    internal static string Describe(NightOrderEntry entry) =>
        entry.Character is { } character ? $"{entry.Kind}:{character}" : entry.Kind.ToString();

    /// <summary>把整条序列转成可断言的文本数组。</summary>
    internal static string[] DescribeAll(IEnumerable<NightOrderEntry> entries) =>
        [.. entries.Select(Describe)];
}
