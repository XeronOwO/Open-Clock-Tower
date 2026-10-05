using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 契约覆盖台账：首版花名册的**每一个**角色都必须落在某一族契约的登记面上，
/// 且夜晚顺序表上的每个行动格都能在建表期取到契约。
/// </summary>
/// <remarks>
/// <para>
/// 依据：架构 §2.6「能力边界」与 `done/juggler-and-savant-day-abilities.md` 的收口结论
/// （`DayActions` 名单现在**全部已实现**，`legality.day_contract_missing` 在首版花名册里
/// 已无可触发的角色）。原先那句"逐角色实现仍按票分批补"只能靠人眼维持——这里换成会失败的测试：
/// 漏登记一个角色，或者往顺序表加一格却忘了写契约，都会在这里红，而不是等到说书人开夜时
/// 撞上 `plan.contract_missing` / `legality.day_contract_missing`。
/// </para>
/// <para>
/// 三族归属（互不排斥，一个角色可以同时属于多族）：
/// ① **夜晚行动格** → <see cref="NightActions"/> 的按键目录（建表取提示、结算取契约）；
/// ② **夜晚触发格** → <see cref="RoleContracts.EventTriggers"/> 里的触发器（进入时只标记时机，
///   不进建表的行动契约闸）；
/// ③ **白天相关** → <see cref="DayActions"/> 的覆盖名单。
/// 一个角色三族都不占，等于它的能力没有任何实现面——那正是本门禁要拦的红。
/// </para>
/// </remarks>
public sealed class CharacterContractCoverageTests
{
    private static readonly NightOrderVariant[] Variants =
        [NightOrderVariant.Original, NightOrderVariant.Recommended];

    private static readonly GamePhase[] NightPhases = [GamePhase.FirstNight, GamePhase.OtherNight];

    /// <summary>顺序表上的角色条目（按种类过滤），两个阶段两个口径取并集。</summary>
    private static SortedSet<string> CharactersOfKind(NightOrderEntryKind kind) =>
        [.. NightPhases
            .SelectMany(phase => Variants.SelectMany(variant => NightOrderTable.For(phase, variant)))
            .Where(entry => entry.Kind == kind)
            .Select(entry => entry.Character!.Value.Value)];

    /// <summary>
    /// 夜晚顺序表上的每个**行动格**都必须能取到提示契约与结算契约。
    /// 缺失即意味着带该角色的局开不了夜（建表期 `plan.contract_missing`）——那不该等到运行时才发现。
    /// </summary>
    [Theory]
    [InlineData(NightOrderVariant.Original)]
    [InlineData(NightOrderVariant.Recommended)]
    public void EveryNightActionSlot_HasBothContracts(NightOrderVariant variant)
    {
        var missing = new List<string>();

        foreach (var phase in NightPhases)
        {
            foreach (var entry in NightOrderTable.For(phase, variant))
            {
                if (entry.Kind != NightOrderEntryKind.CharacterAction)
                {
                    continue;
                }

                var character = entry.Character!.Value;
                if (NightActions.Default.Find(character) is null)
                {
                    missing.Add($"{character.Value} 缺提示契约（INightAction）");
                }

                if (NightActions.Resolutions.Find(character) is null)
                {
                    missing.Add($"{character.Value} 缺结算契约（IAbilityResolution）");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "夜晚行动格取不到契约时，带该角色的局会被建表期显式拒绝（plan.contract_missing）："
            + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// 夜晚顺序表上的**触发格**恰好是这三名死亡触发角色：理发师 / 心上人 / 贤者。
    /// 它们不进建表的行动契约闸，实现面在 <see cref="RoleContracts.EventTriggers"/>
    /// （`BarberNightTrigger` / `SweetheartDeathTrigger` / `SageNightTrigger`）。
    /// 新增触发格会让本断言红——那是提醒作者同步登记触发器，而不是"顺序表悄悄多一格"。
    /// </summary>
    [Fact]
    public void NightTriggerSlots_AreTheThreeDeathTriggerCharacters()
    {
        Assert.Equal(
            ["barber", "sage", "sweetheart"],
            CharactersOfKind(NightOrderEntryKind.CharacterTrigger));
    }

    /// <summary>
    /// 白天契约名单的两份子表必须**完全相等**：与白天相关的角色全部已实现，
    /// 且没有"已覆盖却不在白天相关名单里"的分叉。
    /// 名单是"未实现就拒绝开白天"的输入，漏一个角色 = 白天静默跳过一条规则。
    /// </summary>
    [Fact]
    public void DayContractList_IsFullyCovered()
    {
        var uncovered = SectsAndVioletsRoster.All
            .Where(DayActions.IsDayRelevant)
            .Where(character => !DayActions.IsCovered(character))
            .Select(character => character.Value)
            .ToArray();

        Assert.True(
            uncovered.Length == 0,
            "与白天相关但契约未实现的角色在场时开白天会被拒绝（legality.day_contract_missing）："
            + string.Join("、", uncovered));

        var strayed = SectsAndVioletsRoster.All
            .Where(character => !DayActions.IsDayRelevant(character))
            .Where(DayActions.IsCovered)
            .Select(character => character.Value)
            .ToArray();

        Assert.True(
            strayed.Length == 0,
            "已登记覆盖却不在白天相关名单里的角色：两份子表分叉，"
            + "`IsCovered` 再也拦不住任何东西："
            + string.Join("、", strayed));
    }

    /// <summary>
    /// 首版花名册 30 个角色（25 非旅行者 + 5 旅行者）**无一遗漏**地落在三族归属里。
    /// 这条把架构 §2.6 的"全部 30 个角色已覆盖"变成可失败的判据。
    /// </summary>
    [Fact]
    public void EveryRosterCharacter_IsCoveredBySomeContractFamily()
    {
        var nightActions = CharactersOfKind(NightOrderEntryKind.CharacterAction);
        var nightTriggers = CharactersOfKind(NightOrderEntryKind.CharacterTrigger);
        var dayRelevant = SectsAndVioletsRoster.All
            .Where(DayActions.IsDayRelevant)
            .Select(character => character.Value)
            .ToHashSet(StringComparer.Ordinal);

        var orphans = SectsAndVioletsRoster.All
            .Where(character =>
                !nightActions.Contains(character.Value)
                && !nightTriggers.Contains(character.Value)
                && !dayRelevant.Contains(character.Value))
            .Select(character => character.Value)
            .ToArray();

        Assert.True(
            orphans.Length == 0,
            $"花名册角色没有任何契约族归属（首版 30 人应全覆盖）：{string.Join("、", orphans)}");
    }
}
