using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 仓库级门禁：提交进版本库的文本文件里不得出现机器绝对路径。
/// </summary>
/// <remarks>
/// 依据 <c>AGENTS.md</c>「绝不提交任何机器绝对路径」。绝对路径会让仓库在别人机器上
/// 直接跑不起来，也会泄漏贡献者的本地目录结构。
/// 本地路径只应写进 gitignored 的 <c>AGENTS.local.md</c>，或写成 <c>&lt;占位符&gt;</c>。
/// </remarks>
public sealed partial class RepositoryGateTests
{
    /// <summary>
    /// 在一行里写上这个标记，表示"本行的路径字面量是有意为之"。
    /// 门禁自身的模式定义需要它；任何其它使用都必须能被审查者一眼看到，
    /// 所以标记是明文、可 grep 的，不做隐藏。
    /// </summary>
    private const string LiteralMarker = "path-literal-ok";

    [Fact]
    public void TrackedTextFiles_ContainNoMachineAbsolutePaths()
    {
        var files = RepositoryLayout.EnumerateTrackedTextFiles();
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var relativePath in files)
        {
            var lineNumber = 0;
            foreach (var line in File.ReadLines(RepositoryLayout.PathOf(relativePath)))
            {
                lineNumber++;
                if (line.Contains(LiteralMarker, StringComparison.Ordinal))
                {
                    continue;
                }

                if (MachineAbsolutePath().IsMatch(line))
                {
                    violations.Add($"{relativePath}:{lineNumber}");
                }
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "版本库里出现了机器绝对路径（依据 AGENTS.md「绝不提交任何机器绝对路径」）。" + Environment.NewLine
            + "本地路径只应写进 gitignored 的 AGENTS.local.md，或写成 <占位符>。" + Environment.NewLine
            + report);
    }

    /// <summary>
    /// 盘符路径（一个字母后紧跟冒号再接分隔符）与 UNC 前缀（两个反斜杠后接主机名）。
    /// </summary>
    /// <remarks>
    /// 前置否定环视是为了放过 URL 的协议部分——协议名那个字母前面是别的字母，
    /// 因此不会被误判成盘符。同理，文本里的中文冒号也不参与匹配。 path-literal-ok
    /// </remarks>
    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Za-z]:[\\/]|\\\\[A-Za-z0-9]", RegexOptions.None)]
    private static partial Regex MachineAbsolutePath();
}
