using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 仓库级门禁：提交进版本库的文本文件里不得出现**真机器的指纹**。
/// </summary>
/// <remarks>
/// <para>
/// 依据根 <c>AGENTS.md</c>「绝不提交任何机器绝对路径」与用户级 <c>AGENTS.md</c>「机器信息红线」
/// （2026-10-06 定为红线：真实 IP / 主机名 / 账号名 / 可指纹化的运行读数，与路径一样不许进仓库）。
/// 绝对路径会让仓库在别人机器上直接跑不起来，也泄漏贡献者的目录结构；真实地址则直接指向一台
/// 可以被敲门的机器。
/// </para>
/// <para>
/// **这条只是兜底**：它扫的是「被跟踪文件的**内容**」——扫不了提交信息、扫不了历史，也认不出
/// 裸账号名与主机名（那需要语境）。纪律在前，它只防手滑。
/// </para>
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
    public void TrackedTextFiles_ContainNoMachineFingerprint()
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

                var reason = ReasonFor(line);
                if (reason is not null)
                {
                    violations.Add($"{relativePath}:{lineNumber} → {reason}");
                }
            }
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            string.IsNullOrEmpty(report),
            "版本库里出现了真机器的指纹（依据 AGENTS.md「绝不提交任何机器绝对路径」与用户级「机器信息红线」）。" + Environment.NewLine
            + "改法：换成占位符（<APP_DIR> / <SERVER_IP> / <运维账号>），真值只写 gitignored 的 AGENTS.local.md；"
            + "确属有意为之的字面量（如本门禁的模式定义）在该行标 path-literal-ok。" + Environment.NewLine
            + "这条只兜底：提交信息与历史它都扫不到，提交前自己再看一眼 `git diff --cached`。" + Environment.NewLine
            + report);
    }

    /// <summary>判一行里有没有机器指纹；返回命中的那一类（null = 干净）。</summary>
    private static string? ReasonFor(string line)
    {
        if (MachineAbsolutePath().IsMatch(line))
        {
            return "机器绝对路径（盘符 / UNC）";
        }

        if (HomeDirectory().IsMatch(line))
        {
            return "用户 / 家目录路径";
        }

        if (ServiceDirectory().IsMatch(line))
        {
            return "应用 / 服务目录路径";
        }

        foreach (Match match in Address().Matches(line))
        {
            if (IsPublicAddress(match.Value))
            {
                return $"公网 IP（{match.Value}）";
            }
        }

        return null;
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

    /// <summary>用户 / 家目录路径：家目录下的某个真实名字（占位符不算）。</summary>
    /// <remarks>
    /// 前面的负字符组要求这个斜杠是个**路径起点**（而不是 <c>features/home/...</c> 这类正常相对路径
    /// 里的一段）；后面的字母数字要求排除 <c>/home/&lt;名字&gt;</c> 这种占位符写法。 path-literal-ok
    /// </remarks>
    [GeneratedRegex(@"(?:^|[^A-Za-z0-9._-])/(?:home|Users|root)/[A-Za-z0-9]", RegexOptions.None)]
    private static partial Regex HomeDirectory();

    /// <summary>应用 / 服务目录路径：部署目录下的某个真实名字（占位符不算）。</summary>
    /// <remarks>
    /// 与家目录同款：<c>/etc/...</c> 这类通用系统路径**不**在此列——部署文档正当地引用它们
    /// （systemd 单元、nginx 配置都在那儿），真正属于某一台机器的只是"应用放在哪"。
    /// </remarks>
    [GeneratedRegex(@"(?:^|[^A-Za-z0-9._-])/(?:opt|srv|var/www)/[A-Za-z0-9]", RegexOptions.None)]
    private static partial Regex ServiceDirectory();

    /// <summary>IPv4 字面量；是不是"真实可路由"的地址在 <see cref="IsPublicAddress"/> 里判。</summary>
    [GeneratedRegex(@"(?<![0-9.])(?:[0-9]{1,3}\.){3}[0-9]{1,3}(?![0-9.])", RegexOptions.None)]
    private static partial Regex Address();

    /// <summary>
    /// 是不是"真实可路由的"地址：私有段、回环、文档专用段与非法段都不算。
    /// </summary>
    /// <remarks>
    /// 放行 <c>192.0.2.0/24</c> / <c>198.51.100.0/24</c> / <c>203.0.113.0/24</c>：那是标准里
    /// 给文档与示例留的段（RFC 5737），出现它们是正常的；真实机器不会用它们。
    /// 私有段与回环同理——文档里的示例地址、装置里的仿真 origin 都长这样。
    /// </remarks>
    private static bool IsPublicAddress(string value)
    {
        var parts = value.Split('.');
        if (parts.Length != 4 || !parts.All(part => byte.TryParse(part, out _)))
        {
            return false;
        }

        var octets = parts.Select(byte.Parse).ToArray();
        var (first, second) = (octets[0], octets[1]);

        return first switch
        {
            0 or 10 or 127 or 255 => false,
            172 => second is < 16 or > 31,
            192 when second == 168 => false,
            192 when second == 0 && octets[2] == 2 => false,
            198 when second == 51 && octets[2] == 100 => false,
            203 when second == 0 && octets[2] == 113 => false,
            _ => true,
        };
    }
}
