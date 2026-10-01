using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 编译产物层面的依赖方向检查：分层在**程序集元数据**里是否真的成立。
/// </summary>
/// <remarks>
/// <para>
/// 这不是规范门禁那条 csproj 检查的重复。规范门禁读的是工程文件的 XML；
/// 这里读的是**编译出来的程序集引用表**——间接引用、包依赖、MSBuild 传递引用
/// 都可能绕过 XML 那一层，而元数据里有什么就是什么。
/// </para>
/// <para>
/// 它同时证明了一件更根本的事：**内核真的能脱离上层单独加载**。
/// 若内核引用了上层，纯净门禁与内核单测都会失去意义——它们会连带把上层代码一起装进来。
/// </para>
/// </remarks>
public sealed class AssemblyDependencyTests
{
    private static readonly string[] UpperLayerAssemblies =
    [
        "OpenClockTower.Rules",
        "OpenClockTower.Application",
        "OpenClockTower.Contracts",
        "OpenClockTower.Server",
    ];

    [Fact]
    public void KernelAssembly_DoesNotReferenceAnyUpperLayer()
    {
        var kernel = typeof(SeatState).Assembly;

        var offenders = kernel
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && UpperLayerAssemblies.Contains(name, StringComparer.Ordinal))
            .ToList();

        var report = string.Join(", ", offenders);
        Assert.True(
            string.IsNullOrEmpty(report),
            $"内核程序集引用了上层（依据 docs/architecture/current.md §1）。违规引用：{report}");
    }
}
