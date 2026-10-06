using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：命令里**客户端可控的 string 字段**必须在文本登记表里表态（M4 / G-A5-8）。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>docs/security/web-hardening-audit.md</c> 的 G-A5-8：审计点到的缺口是"长度只覆盖一半"——
/// `note` / `reason` / 幂等键连长度都没有。补上限是一次性动作；**防止下次又漏**要靠这条门禁：
/// 任何人给命令加一个 string 字段而没在 <c>CommandTextLimits</c> 里登记（有上限或写明豁免理由），
/// 这里就变红。
/// </para>
/// <para>
/// 为什么是**文本扫描**而不是反射：本工程刻意不引用 <c>src/</c>（见 csproj 注释）——
/// 它要在被测代码编译失败时也能跑，也就必须在"读源码"这条路上做。代价是它只认
/// `public [required] string[?] X { get; init; }` 这种声明形状；换形状（计算属性、字段）
/// 会漏判——所以它守的是"常规写法"，不是"任何写法"。
/// </para>
/// </remarks>
public sealed partial class CommandTextFieldGateTests
{
    [Fact]
    public void EveryStringFieldOnACommand_IsDeclaredInTheTextLimitsTable()
    {
        var declared = DeclaredFields();
        Assert.True(declared.Count >= 17, $"登记表只解析出 {declared.Count} 行，解析器或登记表结构变了");

        var missing = new List<string>();
        foreach (var (type, property, file) in StringFieldsOnCommands())
        {
            if (!declared.Contains($"{type}.{property}"))
            {
                missing.Add($"{type}.{property}（{file}）：既没有长度上限、也没写明豁免理由");
            }
        }

        Assert.True(
            string.IsNullOrEmpty(string.Join(Environment.NewLine, missing)),
            "命令里的客户端可控文本字段必须在 CommandTextLimits 里表态（M4 / G-A5-8）。" + Environment.NewLine
            + "自由文本 → Bounded(nameof(T.X), 上限)；不是自由文本 → Exempt(nameof(T.X), \"为什么\")。"
            + Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryDeclaredField_ExistsOnTheCommand()
    {
        // 反向：登记表里的每一行都要指得到真实属性（写错名字在门槛上就红，不留到运行期）。
        var dangling = new List<string>();
        foreach (var (type, property) in DeclaredFieldsWithType())
        {
            var source = RepositoryLayout.PathOf("src", "OpenClockTower.Application", $"{type}.cs");
            if (!File.Exists(source))
            {
                dangling.Add($"{type}.{property}：src/OpenClockTower.Application/{type}.cs 不存在");
                continue;
            }

            var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(source));
            if (!PropertyPattern(property).IsMatch(code))
            {
                dangling.Add($"{type}.{property}：这个类型上没有对应的 string 属性");
            }
        }

        Assert.True(
            string.IsNullOrEmpty(string.Join(Environment.NewLine, dangling)),
            "文本登记表里有点不到实处的行：" + Environment.NewLine + string.Join(Environment.NewLine, dangling));
    }

    [Fact]
    public void EveryExemption_CarriesAReason()
    {
        // 豁免不是"没人管"：写不出理由就别豁免。理由必须是登记表里的一句非空说明。
        var registry = File.ReadAllText(RegistryPath());
        var exemptions = ExemptionPattern().Matches(registry);
        Assert.True(exemptions.Count >= 3, $"只找到 {exemptions.Count} 条豁免，解析器或登记表结构变了");

        foreach (Match exemption in exemptions)
        {
            var reason = exemption.Groups["reason"].Value;
            Assert.True(
                reason.Trim().Length >= 8,
                $"{exemption.Groups["field"].Value} 的豁免理由太短（\"{reason}\"）：写清为什么它不是自由文本");
        }
    }

    /// <summary>登记表里声明过的字段（<c>nameof(类型.属性)</c>），返回 <c>类型.属性</c> 集合。</summary>
    private static HashSet<string> DeclaredFields() =>
        [.. DeclaredFieldsWithType().Select(item => $"{item.Type}.{item.Property}")];

    private static List<(string Type, string Property)> DeclaredFieldsWithType()
    {
        var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RegistryPath()));
        return
        [
            .. DeclaredFieldPattern().Matches(code)
                .Select(match => (match.Groups["type"].Value, match.Groups["property"].Value)),
        ];
    }

    /// <summary>遍历 <c>*Command.cs</c>，取出每一条 <c>: GameCommand</c> 上的 string 属性。</summary>
    private static List<(string Type, string Property, string File)> StringFieldsOnCommands()
    {
        var found = new List<(string, string, string)>();
        foreach (var relative in RepositoryLayout.EnumerateSourceFiles("src", "OpenClockTower.Application"))
        {
            if (!relative.EndsWith("Command.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relative)));
            if (!code.Contains(": GameCommand", StringComparison.Ordinal))
            {
                continue;
            }

            var type = Path.GetFileNameWithoutExtension(relative);
            foreach (Match match in StringPropertyPattern().Matches(code))
            {
                found.Add((type, match.Groups["property"].Value, relative));
            }
        }

        Assert.True(found.Count >= 17, $"只找到 {found.Count} 个 string 字段，解析器或代码结构变了");
        return found;
    }

    private static string RegistryPath() =>
        RepositoryLayout.PathOf("src", "OpenClockTower.Application", "CommandTextLimits.cs");

    /// <summary>`public [required] string[?] X { get; init; }`。</summary>
    [GeneratedRegex(@"public\s+(?:required\s+)?string\??\s+(?<property>\w+)\s*\{\s*get;\s*init;\s*\}")]
    private static partial Regex StringPropertyPattern();

    /// <summary>登记表里的 `nameof(类型.属性)`。</summary>
    [GeneratedRegex(@"nameof\((?<type>\w+)\.(?<property>\w+)\)")]
    private static partial Regex DeclaredFieldPattern();

    /// <summary>`Exempt(nameof(类型.属性), "理由")`——理由必须是非空字面量。</summary>
    [GeneratedRegex("Exempt\\(\\s*(?<field>nameof\\(\\w+\\.\\w+\\))\\s*,\\s*\"(?<reason>[^\"]+)\"")] // path-literal-ok
    private static partial Regex ExemptionPattern();

    /// <summary>某个属性名在源码里的声明形状（用于反向核对登记表）。</summary>
    private static Regex PropertyPattern(string property) =>
        new($@"public\s+(?:required\s+)?string\??\s+{Regex.Escape(property)}\s*\{{\s*get;\s*init;\s*\}}");
}
