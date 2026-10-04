using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 两口径的差异必须**恰好**是 R-0014 登记的那几处——顺序再漂移时这里先红。
/// 登记差异：首夜「哲学家 vs 爪牙 / 恶魔信息」先后；其他夜「麻脸巫婆」位置；
/// 其他夜恶魔间顺序；以及推荐口径独有的「信息类角色行动开始」标记。
/// </summary>
public sealed class NightOrderVariantDiffTests
{
    /// <summary>首夜：两版均为 14 条，唯一差异是哲学家与信息环节的先后。</summary>
    [Fact]
    public void FirstNight_DiffersOnlyInPhilosopherVersusInfoSectionOrder()
    {
        var original = NightOrderTable.For(GamePhase.FirstNight, NightOrderVariant.Original);
        var recommended = NightOrderTable.For(GamePhase.FirstNight, NightOrderVariant.Recommended);

        Assert.Equal(14, original.Count);
        Assert.Equal(14, recommended.Count);
        Assert.Equal(4, IndexOf(original, "philosopher"));
        Assert.Equal(2, IndexOf(recommended, "philosopher"));

        Assert.Equal(
            NightOrderText.DescribeAll(WithoutCharacters(original, "philosopher")),
            NightOrderText.DescribeAll(WithoutCharacters(recommended, "philosopher")));
    }

    /// <summary>
    /// 其他夜：唯一差异是麻脸巫婆位置、亡骨魔位置，以及推荐口径独有的信息标记；
    /// 剔除这三处后两版逐条一致。
    /// </summary>
    [Fact]
    public void OtherNight_DiffersOnlyInPitHagDemonOrderAndInfoMarker()
    {
        var original = NightOrderTable.For(GamePhase.OtherNight, NightOrderVariant.Original);
        var recommended = NightOrderTable.For(GamePhase.OtherNight, NightOrderVariant.Recommended);

        Assert.Equal(23, original.Count);
        Assert.Equal(24, recommended.Count);
        Assert.Equal(7, IndexOf(original, "pit-hag"));
        Assert.Equal(4, IndexOf(recommended, "pit-hag"));
        Assert.Equal(9, IndexOf(original, "vigormortis"));
        Assert.Equal(11, IndexOf(recommended, "vigormortis"));

        Assert.Equal(
            NightOrderText.DescribeAll(
                WithoutKind(WithoutCharacters(original, "pit-hag", "vigormortis"), NightOrderEntryKind.InformationActionsBegin)),
            NightOrderText.DescribeAll(
                WithoutKind(WithoutCharacters(recommended, "pit-hag", "vigormortis"), NightOrderEntryKind.InformationActionsBegin)));
    }

    /// <summary>「信息类角色行动开始」标记只在推荐口径的其他夜晚出现，位置为贤者之后、筑梦师之前。</summary>
    [Fact]
    public void OtherNight_RecommendedHasTheInformationActionsMarker_AtTheRegisteredPosition()
    {
        var original = NightOrderTable.For(GamePhase.OtherNight, NightOrderVariant.Original);
        var recommended = NightOrderTable.For(GamePhase.OtherNight, NightOrderVariant.Recommended);

        Assert.DoesNotContain(original, entry => entry.Kind == NightOrderEntryKind.InformationActionsBegin);
        var marker = recommended
            .Select((entry, index) => (entry, index))
            .Single(item => item.entry.Kind == NightOrderEntryKind.InformationActionsBegin);

        Assert.Equal(15, marker.index);
        Assert.Equal(14, IndexOf(recommended, "sage"));
        Assert.Equal(16, IndexOf(recommended, "dreamer"));
    }

    private static int IndexOf(IReadOnlyList<NightOrderEntry> entries, string character)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Character is { } candidate && candidate.Value == character)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"顺序表里没有角色 {character}");
    }

    private static IEnumerable<NightOrderEntry> WithoutCharacters(
        IEnumerable<NightOrderEntry> entries,
        params string[] characters) =>
        entries.Where(entry => entry.Character is not { } character
                               || !characters.Contains(character.Value, StringComparer.Ordinal));

    private static IEnumerable<NightOrderEntry> WithoutKind(
        IEnumerable<NightOrderEntry> entries,
        NightOrderEntryKind kind) =>
        entries.Where(entry => entry.Kind != kind);
}
