using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 六状态正交门禁：<c>SeatState</c> 的五个状态属性必须是**纯自动属性**。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/architecture/current.md</c> §2.1 与百科《重要细节》三：状态与玩家绑定，
/// 「在游戏开始后，玩家的不同状态之间相互独立，互不干涉」。一个自定义访问器就足以把两个维度
/// 偷偷绑在一起（例如在 Character 的 init 里重置 Drunk）——这种 bug 要三十步之后才暴露。
/// </para>
/// <para>
/// 有意收窄范围：只检查 <c>SeatState</c> 本身。把「任何成员只要同时写两个维度就违规」做成门禁，
/// 会误伤将来**有意**同时改变两个维度的效果（那是效果语义，不是联合约束）；假红会让门禁被关掉。
/// 行为语义由 <c>SeatStateTests</c> 的断言守住。
/// </para>
/// </remarks>
public sealed partial class SeatStateIndependenceGateTests
{
    private static readonly string[] StateDimensions =
    [
        "Character", "Alignment", "Life", "Drunk", "Poison",
    ];

    [Fact]
    public void SeatState_DeclaresEveryDimensionAsAPlainAutoProperty()
    {
        var path = RepositoryLayout.PathOf("src", "OpenClockTower.Kernel", "SeatState.cs");
        Assert.True(File.Exists(path), "找不到 SeatState.cs：六状态正交门禁无处可查。");

        var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(path));
        var declarations = StatePropertyDeclaration()
            .Matches(code)
            .Select(match => (
                Name: match.Groups["name"].Value,
                Body: NormalizeWhitespace(match.Groups["body"].Value)))
            .ToList();

        var violations = new List<string>();
        foreach (var dimension in StateDimensions)
        {
            var matches = declarations.Where(declaration => declaration.Name == dimension).ToList();
            if (matches.Count != 1)
            {
                violations.Add($"{dimension} → 找到 {matches.Count} 处属性声明，预期恰好 1 处");
                continue;
            }

            if (matches[0].Body != "get; init;")
            {
                violations.Add($"{dimension} → 访问器不是纯自动属性：{{ {matches[0].Body} }}");
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "SeatState 的五个状态维度必须保持纯自动属性（六状态互不干涉，依据百科《重要细节》三）。" + Environment.NewLine
            + "自定义访问器很容易把两个维度耦合起来；如需派生查询，请新增只读属性或独立类型。" + Environment.NewLine
            + report);
    }

    private static string NormalizeWhitespace(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>匹配五个状态属性的声明；<c>body</c> 若含自定义访问器，就不会等于 <c>get; init;</c>。</summary>
    [GeneratedRegex(@"\b(?<name>Character|Alignment|Life|Drunk|Poison)\s*\{(?<body>[^{}]*)\}")]
    private static partial Regex StatePropertyDeclaration();
}
