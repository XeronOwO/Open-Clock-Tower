using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>花名册：首版 30 人（25 非旅行者 + 5 旅行者）、slug 唯一，且夜晚顺序表上出现的角色都在册。</summary>
public sealed class SectsAndVioletsRosterTests
{
    /// <summary>恰好 30 个角色且互不重复（来源：术语表 §9；非旅行者 2026-10-01、旅行者 2026-10-04 已核对）。</summary>
    [Fact]
    public void Roster_HasExactlyThirtyDistinctCharacters()
    {
        Assert.Equal(30, SectsAndVioletsRoster.All.Count);
        Assert.Equal(30, SectsAndVioletsRoster.All.Distinct().Count());
    }

    /// <summary>5 名旅行者与术语表 §9 一致（D-0022 首版纳入）：slug 与中文名精确匹配，类型为 Traveller。</summary>
    [Fact]
    public void Roster_ContainsTheFiveTravellers()
    {
        var travellers = SectsAndVioletsRoster.OfType(CharacterType.Traveller);

        Assert.Equal(["deviant", "bone-collector", "barista", "harlot", "butcher"], travellers.Select(character => character.Value));
        Assert.Equal(
            ["怪咖", "集骨者", "咖啡师", "流莺", "屠夫"],
            travellers.Select(character => SectsAndVioletsRoster.DisplayNameOf(character)));
    }

    /// <summary>顺序表上出现的每个角色都必须能在花名册里查到（两侧数据不许分叉）。</summary>
    [Fact]
    public void EveryNightOrderCharacter_IsInTheRoster()
    {
        foreach (var variant in new[] { NightOrderVariant.Original, NightOrderVariant.Recommended })
        {
            foreach (var phase in new[] { GamePhase.FirstNight, GamePhase.OtherNight })
            {
                foreach (var entry in NightOrderTable.For(phase, variant))
                {
                    if (entry.Character is { } character)
                    {
                        Assert.True(
                            SectsAndVioletsRoster.Contains(character),
                            $"花名册缺少顺序表角色：{character.Value}");
                    }
                }
            }
        }
    }

    /// <summary>花名册外的角色不被接受（分配合法性会据此拒绝）。</summary>
    [Fact]
    public void UnknownCharacter_IsNotInRoster() =>
        Assert.False(SectsAndVioletsRoster.Contains(new CharacterId("not-a-role")));
}
