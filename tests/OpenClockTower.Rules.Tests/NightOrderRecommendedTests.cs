using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 推荐口径的逐条断言：顺序必须与来源一致，不来自记忆（docs/standard/sources.md §5）。
/// 来源：百科《夜晚行动顺序一览》· 2026-10-01 抓取 · 首个夜晚 / 其他夜晚。
/// </summary>
public sealed class NightOrderRecommendedTests
{
    /// <summary>首个夜晚：14 条；哲学家在信息环节之前；旅行者黄昏行动（咖啡师）紧跟 Dusk。</summary>
    [Fact]
    public void FirstNight_MatchesSource()
    {
        string[] expected =
        [
            "Dusk",
            "CharacterAction:barista",
            "CharacterAction:philosopher",
            "MinionInfo",
            "DemonInfo",
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
            NightOrderText.DescribeAll(NightOrderTable.For(GamePhase.FirstNight, NightOrderVariant.Recommended)));
    }

    /// <summary>
    /// 其他夜晚：25 条；麻脸巫婆提前到舞蛇人之前，恶魔顺序为方古→诺-达鲺→涡流→亡骨魔，
    /// 且在贤者之后多一个「信息类角色行动开始」标记；旅行者黄昏行动（咖啡师 → 流莺 → 集骨者）紧跟 Dusk。
    /// </summary>
    [Fact]
    public void OtherNight_MatchesSource()
    {
        string[] expected =
        [
            "Dusk",
            "CharacterAction:barista",
            "CharacterAction:harlot",
            "CharacterAction:bone-collector",
            "CharacterAction:philosopher",
            "CharacterAction:pit-hag",
            "CharacterAction:snake-charmer",
            "CharacterAction:witch",
            "CharacterAction:cerenovus",
            "CharacterAction:fang-gu",
            "CharacterAction:no-dashii",
            "CharacterAction:vortox",
            "CharacterAction:vigormortis",
            "CharacterTrigger:barber",
            "CharacterTrigger:sweetheart",
            "CharacterTrigger:sage",
            "InformationActionsBegin",
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
            NightOrderText.DescribeAll(NightOrderTable.For(GamePhase.OtherNight, NightOrderVariant.Recommended)));
    }
}
