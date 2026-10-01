namespace OpenClockTower.Kernel;

/// <summary>
/// 判断挂起请求声明的座位依赖是否已被上游状态变化打破，并写出可读的失效原因。
/// </summary>
/// <remarks>
/// 依据票据「自动步骤机与操作请求」第 5 条：目标玩家死亡、角色变更等上游变化必须**自动作废**
/// 并给出原因，不得静默丢弃。未声明的维度不约束（null = 不检查）；
/// 只对**本次真正观测到**的维度判定，不拿没观测的维度去编造结论（六维度相互独立）。
/// </remarks>
public static class SeatDependencyCheck
{
    /// <summary>找出第一条被打破的依赖；一条都没有被打破时返回 null。</summary>
    /// <param name="dependencies">请求声明的依赖集合。</param>
    /// <param name="input">本次观测到的座位状态变化。</param>
    public static SeatDependency? FirstViolated(
        IReadOnlyList<SeatDependency> dependencies,
        SeatStateChangedInput input)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(input);

        return dependencies.FirstOrDefault(dependency => IsViolated(dependency, input));
    }

    /// <summary>写出失效原因：哪个座位、哪一维、现状与要求各是什么。</summary>
    /// <param name="dependency">被打破的依赖。</param>
    /// <param name="input">本次观测到的座位状态变化。</param>
    public static string Describe(SeatDependency dependency, SeatStateChangedInput input)
    {
        ArgumentNullException.ThrowIfNull(dependency);
        ArgumentNullException.ThrowIfNull(input);

        var parts = new List<string>();
        if (input.Life is { } life && dependency.RequiredLife is { } requiredLife && life != requiredLife)
        {
            parts.Add($"生死 {life} ≠ 要求 {requiredLife}");
        }

        if (input.Character is { } character
            && dependency.RequiredCharacter is { } requiredCharacter
            && character != requiredCharacter)
        {
            parts.Add($"角色 {character} ≠ 要求 {requiredCharacter}");
        }

        return $"座位 {dependency.Seat} 的状态变化使请求失去意义（{string.Join("；", parts)}）";
    }

    private static bool IsViolated(SeatDependency dependency, SeatStateChangedInput input) =>
        dependency.Seat == input.Seat
        && ((dependency.RequiredLife is { } requiredLife && input.Life is { } life && life != requiredLife)
            || (dependency.RequiredCharacter is { } requiredCharacter
                && input.Character is { } character
                && character != requiredCharacter));
}
