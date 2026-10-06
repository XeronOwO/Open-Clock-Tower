using System.Diagnostics;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 定位仓库根，并给出"提交进版本库的文件"这一集合。
/// </summary>
/// <remarks>
/// <para>
/// 门禁一律只扫描仓库内的相对路径：门禁自己也不许依赖机器绝对路径，
/// 否则它会在别人机器上因为路径不存在而空转通过——那比失败更危险。
/// </para>
/// <para>
/// "提交进版本库的文件"刻意**问 git**（<c>git ls-files</c>），而不是遍历文件系统：
/// 遍历会把 gitignored 的本地文件（如 <c>AGENTS.local.md</c>，按约定正是放机器路径的地方）
/// 一并扫进来，于是门禁一边要求"本地路径写进 AGENTS.local.md"、
/// 一边又把它报成违规——这种自相矛盾的门禁会很快被关掉。
/// </para>
/// </remarks>
internal static class RepositoryLayout
{
    private const string SolutionFileName = "OpenClockTower.slnx";

    private static readonly string[] TextExtensions =
    [
        ".cs", ".csproj", ".props", ".targets", ".slnx", ".md", ".json",
        ".yml", ".yaml", ".ps1", ".sh", ".editorconfig", ".gitignore", ".gitattributes",
        // 部署模板也是随包发给陌生人的文本（2026-10-06 补：它此前不在扫描范围内，
        // 于是"模板里写死了某台机器的路径"这件事门禁看不见——模板与文档一样要按同一把尺子扫）。
        ".template",
    ];

    private static readonly string[] FileNameOnlyTextFiles =
    [
        ".editorconfig", ".gitignore", ".gitattributes", ".gitkeep",
    ];

    /// <summary>仓库根路径。仅在本进程内用于拼路径，绝不写进任何被扫描的文件。</summary>
    internal static string Root { get; } = Locate();

    /// <summary>把仓库根之下的若干段拼成一个路径。</summary>
    internal static string PathOf(params string[] segments) => Path.Combine([Root, .. segments]);

    /// <summary>把绝对路径转成相对仓库根的展示形式，用于失败信息。</summary>
    internal static string Relative(string absolutePath) => Path.GetRelativePath(Root, absolutePath);

    /// <summary>
    /// 枚举某个子树下的全部**手写** .cs 文件（相对仓库根）。
    /// </summary>
    /// <remarks>
    /// 刻意跳过 <c>bin</c> / <c>obj</c>：那里面是构建产物，包括编译器生成的
    /// <c>*.GlobalUsings.g.cs</c>。扫构建产物会让门禁在"干净"与"已构建"两种状态下结论不同，
    /// 也会把 ImplicitUsings 自动引入的命名空间误报成违规调用。
    /// </remarks>
    internal static IReadOnlyList<string> EnumerateSourceFiles(params string[] segments) =>
        EnumerateFiles("*.cs", segments);

    /// <summary>
    /// 枚举某个子树下匹配给定模式的**手写**文件（相对仓库根）。
    /// </summary>
    /// <remarks>
    /// 前端（<c>web/</c>）的门禁用它找 <c>*.ts</c> / <c>*.vue</c>：
    /// 与 .NET 侧同一套"跳过 bin / obj"的规则，不依赖 git（未跟踪文件也要被扫到）。
    /// </remarks>
    internal static IReadOnlyList<string> EnumerateFiles(string searchPattern, params string[] segments)
    {
        var directory = PathOf(segments);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(directory, searchPattern, SearchOption.AllDirectories)
                .Where(path => !IsBuildOutput(path))
                .Select(Relative)
                .OrderBy(path => path, StringComparer.Ordinal),
        ];
    }

    /// <summary>枚举**被 git 跟踪**的文本文件（相对仓库根）。</summary>
    internal static IReadOnlyList<string> EnumerateTrackedTextFiles()
    {
        var startInfo = new ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 git：仓库门禁需要它判定「提交进版本库的文件」。");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git ls-files 失败（退出码 {process.ExitCode}）：{standardError}");
        }

        return
        [
            .. standardOutput
                .Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Where(IsTextFile)
                // git ls-files 报告的是**索引**：一个已从工作树删除、但删除动作尚未提交的文件
                // （把票据从 todo/ 移到 done/ 就是这种状态）仍然会列在这里。
                // 门禁要扫的是"磁盘上真实存在的被跟踪文件"，否则它会在一个完全正常的
                // 操作序列里抛 FileNotFoundException —— 那会让门禁失去可信度。
                .Where(path => File.Exists(PathOf(path)))
                .OrderBy(path => path, StringComparer.Ordinal),
        ];
    }

    private static bool IsTextFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return FileNameOnlyTextFiles.Contains(fileName, StringComparer.OrdinalIgnoreCase)
               || TextExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBuildOutput(string absolutePath)
    {
        var relative = Path.GetRelativePath(Root, absolutePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Take(segments.Length - 1)
            .Any(segment => segment is "bin" or "obj");
    }

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"向上未找到 {SolutionFileName}，无法确定仓库根。门禁拒绝在未知根目录下运行。");
    }
}
