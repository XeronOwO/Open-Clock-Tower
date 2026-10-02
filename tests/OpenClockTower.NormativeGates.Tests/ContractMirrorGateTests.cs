using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：前端手写的契约镜像是**生成物缺失期间的权宜之计**，必须由测试逐字段对账。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0004「禁止手工维护两份 DTO」与 D-0018：镜像漂移不会让任何一侧编译失败——
/// 服务端删掉一个字段，前端还在读它，运行时只表现为"显示 0 / key 冲突"这类静默错误。
/// 2026-10-02 复核实测到一次：<c>web/src/contracts/game.ts</c> 给 <c>InformationResultDto</c>
/// 加了服务端根本没有的 <c>Sequence</c>，并被当成 Vue 列表 key 使用（两条信息同 key）。
/// 这类漂移靠人看是看不出来的，所以写成会失败的测试。
/// </para>
/// <para>
/// 边界：只对账**字段名与形状**（数组 / 可空 / 是否必填），不比对值域与语义；
/// 前端把服务端数据当不可信输入处理（架构 §4.4），因此形状一致是必要条件而非充分条件。
/// </para>
/// </remarks>
public sealed partial class ContractMirrorGateTests
{
    private const int MaxLines = 600;

    /// <summary>`Contracts` 里每个 DTO 都必须在镜像里有同名字段集合；镜像里不许有幽灵字段。</summary>
    [Fact]
    public void Mirror_MatchesContractsFieldByNameAndShape()
    {
        var interfaces = ParseTypeScriptInterfaces(ReadMirror());
        Assert.True(interfaces.Count >= 15, $"镜像只解析出 {interfaces.Count} 个接口，解析器或文件结构变了");

        var mismatches = new List<string>();
        foreach (var (name, fields) in interfaces)
        {
            var source = RepositoryLayout.PathOf("src", "OpenClockTower.Contracts", $"{name}.cs");
            if (!File.Exists(source))
            {
                mismatches.Add($"{name}：镜像里有这个接口，但 src/OpenClockTower.Contracts/{name}.cs 不存在");
                continue;
            }

            var expected = ParseCSharpDto(File.ReadAllText(source));
            if (expected.Count == 0)
            {
                mismatches.Add($"{name}：契约里没解析出任何属性，解析器或 DTO 结构变了");
                continue;
            }

            foreach (var (field, shape) in fields)
            {
                // wire 上是 camelCase，契约里是 PascalCase：先归一化再比名字，否则全是假红。
                if (!expected.TryGetValue(PascalCaseOf(field), out var contract))
                {
                    mismatches.Add($"{name}.{field}：**幽灵字段**——契约里没有这一项");
                    continue;
                }

                if (contract.Shape != shape)
                {
                    mismatches.Add($"{name}.{field}：契约是 {contract.Shape}，镜像是 {shape}");
                }
            }

            foreach (var (field, contract) in expected)
            {
                if (fields.ContainsKey(CamelCaseOf(field)))
                {
                    continue;
                }

                // 只报"必填字段缺失"：可选字段镜像没写等同于 undefined，形状仍一致。
                if (contract.Required)
                {
                    mismatches.Add($"{name}.{field}：契约必填、镜像缺（前端将读不到这一项）");
                }
            }
        }

        var report = string.Join(Environment.NewLine, mismatches);
        Assert.True(
            mismatches.Count == 0,
            "前端契约镜像与后端契约不一致（依据 D-0004 / D-0018：禁止手工维护两份 DTO 而不对账）。"
            + Environment.NewLine
            + report);
    }

    private static string ReadMirror() =>
        File.ReadAllText(RepositoryLayout.PathOf("web", "src", "contracts", "game.ts"));

    /// <summary>wire 上的 camelCase → 契约里的 PascalCase。</summary>
    private static string PascalCaseOf(string camel) =>
        camel.Length == 0 ? camel : char.ToUpperInvariant(camel[0]) + camel[1..];

    /// <summary>契约里的 PascalCase → wire 上的 camelCase。</summary>
    private static string CamelCaseOf(string pascal) =>
        pascal.Length == 0 ? pascal : char.ToLowerInvariant(pascal[0]) + pascal[1..];

    /// <summary>解析 `export interface X { ... }`（叶子 DTO 的平坦字段；嵌套对象属性不受此限）。</summary>
    private static Dictionary<string, Dictionary<string, string>> ParseTypeScriptInterfaces(string source)
    {
        var cleaned = SourceText.StripCommentsAndLiterals(source);
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (Match match in InterfaceDeclaration().Matches(cleaned))
        {
            var body = match.Groups["body"].Value;
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match field in TypeScriptField().Matches(body))
            {
                fields[field.Groups["name"].Value] = ShapeOf(field.Groups["type"].Value.Trim());
            }

            result[match.Groups["name"].Value] = fields;
        }

        return result;
    }

    /// <summary>解析 C# DTO 的属性：名字、类型形状与 `required`。</summary>
    private static Dictionary<string, (string Shape, bool Required)> ParseCSharpDto(string source)
    {
        var cleaned = SourceText.StripCommentsAndLiterals(source);
        var result = new Dictionary<string, (string Shape, bool Required)>(StringComparer.Ordinal);

        foreach (Match match in CSharpProperty().Matches(cleaned))
        {
            var required = match.Groups["required"].Success;
            var type = match.Groups["type"].Value.Trim();
            result[match.Groups["name"].Value] = (ShapeOfCSharpType(type), required);
        }

        return result;
    }

    /// <summary>TypeScript 形状：`T[]` → array、`T | null` → nullable、其余为 value。</summary>
    private static string ShapeOf(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal) || type.StartsWith("readonly ", StringComparison.Ordinal))
        {
            return "array";
        }

        return type.Contains("| null", StringComparison.Ordinal) ? "nullable" : "value";
    }

    /// <summary>C# 形状：`IReadOnlyList&lt;T&gt;` / `T[]` → array、可空标注 → nullable、其余为 value。</summary>
    private static string ShapeOfCSharpType(string type)
    {
        if (type.EndsWith("[]", StringComparison.Ordinal) || type.Contains("IReadOnlyList<", StringComparison.Ordinal))
        {
            return "array";
        }

        return type.EndsWith("?", StringComparison.Ordinal) ? "nullable" : "value";
    }

    [GeneratedRegex(
        @"export\s+interface\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{(?<body>[^{}]*)\}",
        RegexOptions.Singleline)]
    private static partial Regex InterfaceDeclaration();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<optional>\?)?\s*:\s*(?<type>[^;\r\n]+);?$", RegexOptions.Multiline)]
    private static partial Regex TypeScriptField();

    [GeneratedRegex(
        @"(?:public\s+)?(?<required>required\s+)?(?:readonly\s+)?(?<type>[A-Za-z_][A-Za-z0-9_<>,\.\?\s\[\]]*?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*init;\s*\}")]
    private static partial Regex CSharpProperty();
}
