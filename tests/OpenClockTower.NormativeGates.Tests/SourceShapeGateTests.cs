using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 源码形状门禁：一个文件只允许一个顶层类型。
/// </summary>
/// <remarks>
/// 依据 <c>AGENTS.md</c>「架构硬约束」与参考项目 CUO 的约定：文件名 = 类型名。
/// 嵌套类型不受限（它们属于容器），因此正则只在**第 0 列**匹配。
/// </remarks>
public sealed partial class SourceShapeGateTests
{
    [Fact]
    public void SourceFiles_DeclareAtMostOneTopLevelType()
    {
        var files = RepositoryLayout.EnumerateSourceFiles("src").Concat(RepositoryLayout.EnumerateSourceFiles("tests")).ToList();
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var relativePath in files)
        {
            var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            var count = TopLevelTypeDeclaration().Matches(code).Count;
            if (count > 1)
            {
                violations.Add($"{relativePath} → {count} 个顶层类型");
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "一个文件只能有一个顶层类型，文件名即类型名（依据 AGENTS.md 架构硬约束）。" + Environment.NewLine
            + "嵌套辅助类型保留在容器内，不受此限。" + Environment.NewLine
            + report);
    }

    /// <summary>
    /// 只匹配**第 0 列**的类型声明，因此嵌套类型不会命中。
    /// </summary>
    /// <remarks>
    /// 已知边界：清洗器处理不了原生字符串字面量 <c>"""</c>（本项目暂未使用）。
    /// 若将来在源码里用原生字符串嵌入 C# 片段，必须先扩展 <see cref="SourceText"/>。
    /// </remarks>
    [GeneratedRegex(
        @"^(?:(?:public|internal|private|protected|file)\s+)?(?:(?:static|sealed|abstract|partial|readonly|unsafe|new)\s+)*(?:class|record|struct|interface|enum|delegate)\s+[A-Za-z_]",
        RegexOptions.Multiline)]
    private static partial Regex TopLevelTypeDeclaration();
}
