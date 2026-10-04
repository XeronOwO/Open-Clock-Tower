namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：`MalfunctionKind.Barista` 只准出现在**枚举声明**与**呈现映射**里——引擎不得把咖啡师的
/// 效果记成「能力未正常生效」。
/// </summary>
/// <remarks>
/// <para>
/// 依据 <c>rulings.md</c> R-0047 第 5 条与 R-0004：咖啡师的两个效果都是"让能力更有效"
/// （免疫醉酒中毒 + 必定正确信息 / 能力可以生效两次），不产生「未正常生效」，因此**没有任何产出路径**
/// 应写这一类记录。枚举成员之所以保留，是因为事件载荷里的枚举按**数值**持久化——删成员会让其后成员
/// 整体前移、旧日志回放时误读（口径见 R-0047 第 5 条的 2026-10-04 收口说明）。
/// </para>
/// <para>
/// 允许清单只有两处：枚举声明本身、复盘文案的显示映射。新增第三处就会红——那时要么是漏了注释里的
/// 判定，要么是真的写出了失效记录；两种情况都该先读 R-0047 再决定。
/// </para>
/// </remarks>
public sealed class BaristaMalfunctionGateTests
{
    /// <summary>允许提及该枚举成员的文件（相对仓库根，正斜杠写法；与平台无关地比较）。</summary>
    private static readonly string[] AllowedFiles =
    [
        "src/OpenClockTower.Kernel/MalfunctionKind.cs",
        "src/OpenClockTower.Application/ReplayText.cs",
    ];

    /// <summary>引擎里不得出现写这类失效分类的代码（注释与字符串已剥离，只留下真实代码）。</summary>
    [Fact]
    public void Engine_NeverRecordsBaristaAsAMalfunction()
    {
        var files = RepositoryLayout.EnumerateSourceFiles("src");
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var relativePath in files)
        {
            if (AllowedFiles.Contains(relativePath.Replace('\\', '/'), StringComparer.Ordinal))
            {
                continue;
            }

            var code = SourceText.StripCommentsAndLiterals(
                File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            if (code.Contains("MalfunctionKind.Barista", StringComparison.Ordinal))
            {
                violations.Add(relativePath);
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "出现了引用 MalfunctionKind.Barista 的引擎代码（依据 rulings.md R-0047 第 5 条 / R-0004："
            + "咖啡师的效果不产生「能力未正常生效」，引擎不得写这一类记录）。"
            + Environment.NewLine
            + "如果这是新的呈现映射，请把文件加进本门禁的允许清单并在提交信息里说明。"
            + Environment.NewLine
            + report);
    }
}
