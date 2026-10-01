using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 顺序表的结构契约：起止环节、首夜信息环节、角色清单覆盖与查询边界。
/// </summary>
public sealed class NightOrderTableTests
{
    /// <summary>
    /// 《梦殒春宵》25 个角色。
    /// 依据 docs/standard/terminology.md §9（2026-10-01 已逐条核对，禁止用全站页名清单直接匹配）。
    /// </summary>
    private static readonly string[] ScriptCharacters =
    [
        "clockmaker", "dreamer", "snake-charmer", "mathematician", "flowergirl",
        "town-crier", "oracle", "savant", "seamstress", "philosopher",
        "artist", "juggler", "sage",
        "mutant", "sweetheart", "barber", "klutz",
        "evil-twin", "witch", "cerenovus", "pit-hag",
        "fang-gu", "vigormortis", "no-dashii", "vortox",
    ];

    /// <summary>
    /// 没有夜晚行动的角色：能力发生在白天或被处决 / 死亡时，因此不在夜晚顺序表上。
    /// 依据 docs/standard/character-rules.md 对应条目（2026-10-01 抓取）。
    /// </summary>
    private static readonly string[] CharactersWithoutNightAction =
    [
        "artist", "klutz", "mutant", "savant",
    ];

    /// <summary>首夜：以黄昏起、以黎明止，爪牙 / 恶魔信息各恰好一条。</summary>
    [Theory]
    [InlineData(NightOrderVariant.Original)]
    [InlineData(NightOrderVariant.Recommended)]
    public void FirstNight_StartsWithDusk_EndsWithDawn_AndHasBothInfoSections(NightOrderVariant variant)
    {
        var entries = NightOrderTable.For(GamePhase.FirstNight, variant);

        Assert.Equal(NightOrderEntryKind.Dusk, entries[0].Kind);
        Assert.Equal(NightOrderEntryKind.Dawn, entries[^1].Kind);
        Assert.Equal(1, entries.Count(entry => entry.Kind == NightOrderEntryKind.MinionInfo));
        Assert.Equal(1, entries.Count(entry => entry.Kind == NightOrderEntryKind.DemonInfo));
        Assert.DoesNotContain(entries, entry => entry.Kind == NightOrderEntryKind.InformationActionsBegin);
    }

    /// <summary>其他夜晚：以黄昏起、以黎明止；爪牙 / 恶魔信息只在首夜。</summary>
    [Theory]
    [InlineData(NightOrderVariant.Original)]
    [InlineData(NightOrderVariant.Recommended)]
    public void OtherNight_StartsWithDusk_EndsWithDawn_AndHasNoFirstNightInfoSections(NightOrderVariant variant)
    {
        var entries = NightOrderTable.For(GamePhase.OtherNight, variant);

        Assert.Equal(NightOrderEntryKind.Dusk, entries[0].Kind);
        Assert.Equal(NightOrderEntryKind.Dawn, entries[^1].Kind);
        Assert.DoesNotContain(
            entries,
            entry => entry.Kind is NightOrderEntryKind.MinionInfo or NightOrderEntryKind.DemonInfo);
    }

    /// <summary>同一阶段内同一角色不会出现两次（角色唯一，隐性规则 §4）。</summary>
    [Theory]
    [InlineData(NightOrderVariant.Original)]
    [InlineData(NightOrderVariant.Recommended)]
    public void CharacterActions_AreUniqueWithinEachPhase(NightOrderVariant variant)
    {
        foreach (var phase in new[] { GamePhase.FirstNight, GamePhase.OtherNight })
        {
            var actors = NightOrderTable.For(phase, variant)
                .Select(entry => entry.Character is { } character ? character.Value : null)
                .Where(slug => slug is not null)
                .ToList();

            Assert.Equal(actors.Count, actors.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>表上的角色必须来自剧本 25 人，且覆盖全部 21 个有夜晚行动的角色。</summary>
    [Theory]
    [InlineData(NightOrderVariant.Original)]
    [InlineData(NightOrderVariant.Recommended)]
    public void CharacterActions_ComeFromScript_AndCoverEveryNightActor(NightOrderVariant variant)
    {
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var phase in new[] { GamePhase.FirstNight, GamePhase.OtherNight })
        {
            foreach (var entry in NightOrderTable.For(phase, variant))
            {
                if (entry.Character is not { } character)
                {
                    continue;
                }

                Assert.Contains(character.Value, ScriptCharacters);
                seen.Add(character.Value);
            }
        }

        var expected = ScriptCharacters.Except(CharactersWithoutNightAction, StringComparer.Ordinal).ToList();
        Assert.True(
            seen.SetEquals(expected),
            $"夜晚顺序表应覆盖 {expected.Count} 个有夜晚行动的角色；实际覆盖 {seen.Count} 个：{string.Join(",", seen)}");
    }

    /// <summary>白天 / 结算中没有夜晚顺序表，查询必须显式失败而不是返回空表。</summary>
    [Fact]
    public void DayAndResolving_HaveNoNightOrder()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NightOrderTable.For(GamePhase.Day, NightOrderVariant.Original));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NightOrderTable.For(GamePhase.Resolving, NightOrderVariant.Recommended));
    }

    /// <summary>没有角色信息的 CharacterAction 是坏数据：构造入口必须拒绝。</summary>
    [Fact]
    public void Step_RejectsCharacterActionKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => NightOrderEntry.Step(NightOrderEntryKind.CharacterAction));
    }
}
