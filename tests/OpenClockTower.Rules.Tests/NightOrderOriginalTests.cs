using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 原本口径的逐条断言：顺序必须与来源一致，不来自记忆（docs/standard/sources.md §5）。
/// 来源：百科《梦殒春宵》· 2026-10-01 抓取 · 夜晚顺序表。
/// </summary>
public sealed class NightOrderOriginalTests
{
    /// <summary>首个夜晚：14 条，信息环节在哲学家之前；旅行者黄昏行动（咖啡师）紧跟 Dusk。</summary>
    [Fact]
    public void FirstNight_MatchesSource()
    {
        string[] expected =
        [
            "Dusk",
            "CharacterAction:barista",
            "MinionInfo",
            "DemonInfo",
            "CharacterAction:philosopher",
            "CharacterAction:snake-charmer",
            "CharacterAction:evil-twin",
            "CharacterAction:witch",
            "CharacterAction:cerenovus",
            "CharacterAction:clockmaker",
            "CharacterAction:dreamer",
            "CharacterAction:seamstress",
            "CharacterAction:mathematician",
            "Dawn",
        ];

        Assert.Equal(
            expected,
            NightOrderText.DescribeAll(NightOrderTable.For(GamePhase.FirstNight, NightOrderVariant.Original)));
    }

    /// <summary>
    /// 其他夜晚：24 条；麻脸巫婆在洗脑师之后、恶魔顺序为方古→亡骨魔→诺-达鲺→涡流；
    /// 旅行者黄昏行动（咖啡师 → 流莺）紧跟 Dusk，与推荐口径同改（票据 D5 / R-0051 / R-0052）。
    /// </summary>
    [Fact]
    public void OtherNight_MatchesSource()
    {
        string[] expected =
        [
            "Dusk",
            "CharacterAction:barista",
            "CharacterAction:harlot",
            "CharacterAction:philosopher",
            "CharacterAction:snake-charmer",
            "CharacterAction:witch",
            "CharacterAction:cerenovus",
            "CharacterAction:pit-hag",
            "CharacterAction:fang-gu",
            "CharacterAction:vigormortis",
            "CharacterAction:no-dashii",
            "CharacterAction:vortox",
            "CharacterTrigger:barber",
            "CharacterTrigger:sweetheart",
            "CharacterTrigger:sage",
            "CharacterAction:dreamer",
            "CharacterAction:flowergirl",
            "CharacterAction:town-crier",
            "CharacterAction:oracle",
            "CharacterAction:seamstress",
            "CharacterAction:juggler",
            "CharacterAction:mathematician",
            "Dawn",
        ];

        Assert.Equal(
            expected,
            NightOrderText.DescribeAll(NightOrderTable.For(GamePhase.OtherNight, NightOrderVariant.Original)));
    }
}
