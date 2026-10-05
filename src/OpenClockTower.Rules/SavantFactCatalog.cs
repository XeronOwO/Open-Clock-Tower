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

        return candidates;
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
            OppositeGroup = definition.OppositeGroup,
        };
    }

    /// <summary>无参数事实只给一个空参数（候选仍然只有一条）；有参数事实的参数表由定义自己给。</summary>
    private static IReadOnlyList<string?> ParametersOf(SavantFactDefinition definition, SavantFactWorld world)
    {
        var parameters = definition.Parameters(world);
        return parameters.Count == 0 ? [null] : [.. parameters.Cast<string?>()];
    }
}
