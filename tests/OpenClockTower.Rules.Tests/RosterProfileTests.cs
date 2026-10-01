using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 花名册档案：25 个角色的类型与中文名（术语表 §9 的代码侧落点）。
/// </summary>
public sealed class RosterProfileTests
{
    [Fact]
    public void RosterHasTwentyFiveRolesWithTypesAndNames()
    {
        Assert.Equal(25, SectsAndVioletsRoster.All.Count);

        foreach (var character in SectsAndVioletsRoster.All)
        {
            Assert.NotNull(SectsAndVioletsRoster.TypeOf(character));
            Assert.False(string.IsNullOrWhiteSpace(SectsAndVioletsRoster.DisplayNameOf(character)));
        }
    }

    [Fact]
    public void TypeCountsMatchTheScript()
    {
        Assert.Equal(13, SectsAndVioletsRoster.OfType(CharacterType.Townsfolk).Count);
        Assert.Equal(4, SectsAndVioletsRoster.OfType(CharacterType.Outsider).Count);
        Assert.Equal(4, SectsAndVioletsRoster.OfType(CharacterType.Minion).Count);
        Assert.Equal(4, SectsAndVioletsRoster.OfType(CharacterType.Demon).Count);
    }

    [Fact]
    public void KnownRolesCarryExpectedProfile()
    {
        Assert.Equal(CharacterType.Demon, SectsAndVioletsRoster.TypeOf(new CharacterId("no-dashii")));
        Assert.Equal("诺-达鲺", SectsAndVioletsRoster.DisplayNameOf(new CharacterId("no-dashii")));
        Assert.Equal(CharacterType.Townsfolk, SectsAndVioletsRoster.TypeOf(new CharacterId("dreamer")));
        Assert.Equal("筑梦师", SectsAndVioletsRoster.DisplayNameOf(new CharacterId("dreamer")));
    }

    [Fact]
    public void UnknownCharacter_IsNotGuessed()
    {
        var unknown = new CharacterId("not-a-role");

        Assert.False(SectsAndVioletsRoster.Contains(unknown));
        Assert.Null(SectsAndVioletsRoster.TypeOf(unknown));
        Assert.Null(SectsAndVioletsRoster.DisplayNameOf(unknown));
    }
}
