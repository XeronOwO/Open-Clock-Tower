using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 候选事实库（R-0057-C）：把 A–E 五组事实合成"此刻账下的候选清单"，并按编码回查。
/// </summary>
/// <remarks>
/// 候选 = 定义 × 可选取值，逐条求值；**判不了的（求值返回 null）不出现**在候选里（D-0015：不猜）。
/// 说书人提交时按当时的账**重新求值**（票据验收矩阵第 9 行），因此这里回查走的是同一条路径。
/// </remarks>
internal static class SavantFactCatalog
{
    /// <summary>全部事实定义（分组顺序 = 说书人端的分栏顺序）。</summary>
    internal static IReadOnlyList<SavantFactDefinition> Definitions { get; } =
    [
        .. SavantSeatFacts.All,
        .. SavantNumberFacts.All,
        .. SavantChangeFacts.All,
        .. SavantStatusFacts.All,
        .. SavantAccusationFacts.All,
    ];

    /// <summary>此刻账下的全部候选（顺序 = 定义顺序 × 参数顺序）。</summary>
    internal static IReadOnlyList<SavantFactCandidate> Candidates(SavantFactWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var candidates = new List<SavantFactCandidate>();
        foreach (var definition in Definitions)
        {
            foreach (var parameter in ParametersOf(definition, world))
            {
                if (Describe(world, definition, parameter) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }
        }

        GuardExclusiveValues(candidates);
        return candidates;
    }

    /// <summary>
    /// 守住"取值互斥"这条不变量：声明了 <see cref="SavantFactDefinition.ExclusiveValues"/> 的事实，
    /// 同一时刻**至多一个取值为真**。违反了当场抛错——不是把问题留给说书人端
    /// （前端会把整族候选互相灰掉，说书人一个都点不动，而且看不出为什么）。
    /// </summary>
    private static void GuardExclusiveValues(IReadOnlyList<SavantFactCandidate> candidates)
    {
        var offenders = candidates
            .Where(candidate => candidate.ExclusiveValues && candidate.Truth == OptionTruth.True)
            .GroupBy(candidate => candidate.Code, StringComparer.Ordinal)
            .Where(family => family.Count() > 1)
            .Select(family => $"{family.Key}（{string.Join(" / ", family.Select(item => item.Parameter))}）")
            .ToArray();

        if (offenders.Length > 0)
        {
            throw new InvalidOperationException(
                "候选事实库定义错误：声明了「取值互斥」的事实同时有多个取值为真——"
                + $"{string.Join("；", offenders)}。互斥的取值必须恰好覆盖所有情形（穷尽且互不重叠）；"
                + "措辞重叠的定义要拆开或改成互不重叠的取值（R-0057-C）。");
        }
    }

    /// <summary>
    /// 按裁定文本里的取值回查候选（**按提交时刻的账重新求值**）；
    /// 未知编码 / 参数不认识 / 此刻判不了一律返回 null（由调用方转成可读拒绝）。
    /// </summary>
    internal static SavantFactCandidate? Resolve(SavantFactWorld world, string value)
    {
        ArgumentNullException.ThrowIfNull(world);

        if (!SavantFactFormat.TryParse(value, out var code, out var parameter))
        {
            return null;
        }

        var definition = Definitions.FirstOrDefault(candidate => candidate.Code == code);
        return definition is null ? null : Describe(world, definition, parameter);
    }

    private static SavantFactCandidate? Describe(
        SavantFactWorld world,
        SavantFactDefinition definition,
        string? parameter)
    {
        if (definition.Evaluate(world, parameter) is not { } evaluation)
        {
            return null;
        }

        return new SavantFactCandidate
        {
            Value = SavantFactFormat.Format(definition.Code, parameter),
            Code = definition.Code,
            Parameter = parameter,
            Text = evaluation.Text,
            Group = definition.Group,
            Truth = evaluation.Truth,
            HighIntensity = definition.HighIntensity,
            ExclusiveValues = definition.ExclusiveValues,
            ExclusionGroup = definition.ExclusionGroupOf?.Invoke(world, parameter)
                ?? definition.ExclusionGroup
                ?? (definition.ExclusiveValues ? definition.Code : null),
        };
    }

    /// <summary>无参数事实只给一个空参数（候选仍然只有一条）；有参数事实的参数表由定义自己给。</summary>
    private static IReadOnlyList<string?> ParametersOf(SavantFactDefinition definition, SavantFactWorld world)
    {
        var parameters = definition.Parameters(world);
        return parameters.Count == 0 ? [null] : [.. parameters.Cast<string?>()];
    }
}
