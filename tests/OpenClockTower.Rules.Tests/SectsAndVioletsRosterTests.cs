using OpenClockTower.Kernel;

namespace OpenClockTower.Rules.Tests;

/// <summary>花名册：首版 25 人、slug 唯一，且夜晚顺序表上出现的角色都在册。</summary>
public sealed class SectsAndVioletsRosterTests
{
    /// <summary>恰好 25 个角色且互不重复（来源：术语表 §9，2026-10-01 已核对）。</summary>
    [Fact]
    public void Roster_HasExactlyTwentyFiveDistinctCharacters()
    {
        Assert.Equal(25, SectsAndVioletsRoster.All.Count);
        Assert.Equal(25, SectsAndVioletsRoster.All.Distinct().Count());
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
