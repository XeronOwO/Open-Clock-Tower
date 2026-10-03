using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：花名册两侧（服务端《梦殒春宵》权威数据 ↔ 前端呈现副本）必须逐项对账——
/// slug、中文名、类型与「设置调整」。
/// </summary>
/// <remarks>
/// <para>
/// 依据 R-0042 第 3 条的处置（花名册权威落点）：服务端 <c>SectsAndVioletsRoster</c> 是权威，
/// <c>web/src/display/labels.ts</c> 只做呈现副本。两份手工维护的数据不会让任何一侧编译失败——
/// 2026-10-04 实测踩过一次：亡骨魔的 <c>[-1 外来者]</c> 只存在于百科与规则侧，前端根本没有，
/// 设置界面因此静默漏提示。这类漂移写成会失败的测试。
/// </para>
/// <para>边界：只对账花名册数据（slug / 名 / 类型 / 修正），不比对 UI 文案与实现。</para>
/// </remarks>
public sealed partial class RosterMirrorGateTests
{
    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.Ordinal)
    {
        ["Townsfolk"] = "镇民",
        ["Outsider"] = "外来者",
        ["Minion"] = "爪牙",
        ["Demon"] = "恶魔",
    };

    [Fact]
    public void FrontendRosterMirrorsTheServerRoster()
    {
        var server = ParseServerRoster(
            File.ReadAllText(RepositoryLayout.PathOf("src", "OpenClockTower.Rules", "SectsAndVioletsRoster.cs")));
        var web = ParseWebRoster(
            File.ReadAllText(RepositoryLayout.PathOf("web", "src", "display", "labels.ts")));

        Assert.True(server.Count == 25, $"服务端花名册解析出 {server.Count} 条，解析器或文件结构变了");
        Assert.True(web.Count == 25, $"前端花名册解析出 {web.Count} 条，解析器或文件结构变了");

        var mismatches = new List<string>();

        var serverSlugs = string.Join(", ", server.Select(entry => entry.Slug));
        var webSlugs = string.Join(", ", web.Select(entry => entry.Slug));
        if (!string.Equals(serverSlugs, webSlugs, StringComparison.Ordinal))
        {
            mismatches.Add($"slug 集合或顺序不一致（顺序即术语表 §9 顺序）：服务端 [{serverSlugs}]，前端 [{webSlugs}]");
        }

        var webBySlug = web.ToDictionary(entry => entry.Slug, StringComparer.Ordinal);
        foreach (var entry in server)
        {
            if (!webBySlug.TryGetValue(entry.Slug, out var mirror))
            {
                mismatches.Add($"{entry.Slug}：前端花名册里没有这个角色");
                continue;
            }

            if (!string.Equals(entry.Name, mirror.Name, StringComparison.Ordinal))
            {
                mismatches.Add($"{entry.Slug}：中文名不一致（服务端「{entry.Name}」/ 前端「{mirror.Name}」）");
            }

            if (!TypeLabels.TryGetValue(entry.Type, out var typeLabel) || typeLabel != mirror.Type)
            {
                mismatches.Add($"{entry.Slug}：类型不一致（服务端 {entry.Type} / 前端 {mirror.Type}）");
            }

            var expected = Normalize(BuildModifier(entry.Adjustments));
            var actual = Normalize(mirror.ModifierText ?? string.Empty);
            if (expected.Length == 0 && actual.Length > 0)
            {
                mismatches.Add($"{entry.Slug}：服务端没有设置调整，前端却写了「{mirror.ModifierText}」");
            }
            else if (expected.Length > 0 && !actual.Contains(expected, StringComparison.Ordinal))
            {
                mismatches.Add(
                    $"{entry.Slug}：设置调整不一致——服务端要求文案含 {expected}，前端是「{mirror.ModifierText ?? "(缺)"}」");
            }
        }

        foreach (var extra in web.Where(mirror => server.All(entry => entry.Slug != mirror.Slug)))
        {
            mismatches.Add($"{extra.Slug}：前端有、服务端没有的角色");
        }

        Assert.True(
            mismatches.Count == 0,
            "花名册两侧漂移（R-0042 第 3 条：服务端权威、前端只做呈现副本）。"
            + Environment.NewLine
            + string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>花名册的一条（本门禁只读文本，刻意不引用 src 工程）。</summary>
    /// <param name="Adjustments">服务端专用：结构化设置调整。</param>
    /// <param name="ModifierText">前端专用：呈现副本里的修正文案（没写则为 null）。</param>
    private sealed record Entry(
        string Slug,
        string Name,
        string Type,
        IReadOnlyList<(string Type, int Delta)> Adjustments,
        string? ModifierText);

    private static List<Entry> ParseServerRoster(string source)
    {
        var found = new List<(int Index, Entry Entry)>();

        foreach (Match match in PlainEntry().Matches(source))
        {
            found.Add((match.Index, new Entry(
                match.Groups["slug"].Value,
                match.Groups["name"].Value,
                match.Groups["type"].Value,
                [],
                null)));
        }

        foreach (Match match in AdjustedEntry().Matches(source))
        {
            var adjustments = new List<(string, int)>();
            foreach (Match adjustment in Adjustment().Matches(match.Groups["adjustments"].Value))
            {
                adjustments.Add((
                    adjustment.Groups["type"].Value,
                    int.Parse(adjustment.Groups["delta"].Value, CultureInfo.InvariantCulture)));
            }

            found.Add((match.Index, new Entry(
                match.Groups["slug"].Value,
                match.Groups["name"].Value,
                match.Groups["type"].Value,
                adjustments,
                null)));
        }

        return [.. found.OrderBy(item => item.Index).Select(item => item.Entry)];
    }

    private static List<Entry> ParseWebRoster(string source)
    {
        var entries = new List<Entry>();

        foreach (Match match in WebEntry().Matches(source))
        {
            var tail = match.Groups["tail"].Value;
            var modifier = WebSetupModifier().Match(tail);

            entries.Add(new Entry(
                match.Groups["slug"].Value,
                match.Groups["name"].Value,
                match.Groups["type"].Value,
                [],
                modifier.Success ? modifier.Groups["text"].Value : null));
        }

        return entries;
    }

    /// <summary>服务端修正 → 规范方括号文案（如 `[+1 外来者]`）。</summary>
    private static string BuildModifier(IReadOnlyList<(string Type, int Delta)> adjustments) =>
        string.Concat(adjustments.Select(adjustment =>
        {
            var type = TypeLabels.TryGetValue(adjustment.Type, out var label) ? label : adjustment.Type;
            return $"[{(adjustment.Delta > 0 ? "+" : string.Empty)}{adjustment.Delta} {type}]";
        }));

    private static string Normalize(string text) => text.Replace(" ", string.Empty, StringComparison.Ordinal);

    [GeneratedRegex(@"Plain\(""(?<slug>[a-z0-9-]+)"", CharacterType\.(?<type>\w+), ""(?<name>[^""]*)""\)")]
    private static partial Regex PlainEntry();

    [GeneratedRegex(
        @"new\(new CharacterId\(""(?<slug>[a-z0-9-]+)""\), CharacterType\.(?<type>\w+), ""(?<name>[^""]*)"",\s*\[(?<adjustments>[^\]]*)\]\)",
        RegexOptions.Singleline)]
    private static partial Regex AdjustedEntry();

    [GeneratedRegex(@"new SetupAdjustment\(CharacterType\.(?<type>\w+), (?<delta>-?\d+)\)")]
    private static partial Regex Adjustment();

    [GeneratedRegex(@"\{\s*slug:\s*'(?<slug>[a-z0-9-]+)',\s*name:\s*'(?<name>[^']*)',\s*type:\s*'(?<type>[^']*)'(?<tail>[^}]*)\}")]
    private static partial Regex WebEntry();

    [GeneratedRegex(@"setupModifier:\s*'(?<text>[^']*)'")]
    private static partial Regex WebSetupModifier();
}
