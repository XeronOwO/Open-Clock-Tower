using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 花名册档案：25 个角色的类型、中文名与设置调整（术语表 §9 的代码侧落点；修正口径见 R-0042）。
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
        Assert.Empty(SectsAndVioletsRoster.SetupAdjustmentsOf(unknown));
    }

    [Fact]
    public void OnlyTheTwoKnownDemonsCarrySetupAdjustments()
    {
        // R-0042 依据：百科《设置调整》· 相关角色（基础配置变动）只列了这两条。
        var carriers = SectsAndVioletsRoster.All
            .Where(character => SectsAndVioletsRoster.SetupAdjustmentsOf(character).Count > 0)
            .ToList();

        Assert.Equal([new CharacterId("fang-gu"), new CharacterId("vigormortis")], carriers);
        Assert.Equal(
            [new SetupAdjustment(CharacterType.Outsider, 1)],
            SectsAndVioletsRoster.SetupAdjustmentsOf(new CharacterId("fang-gu")));
        Assert.Equal(
            [new SetupAdjustment(CharacterType.Outsider, -1)],
            SectsAndVioletsRoster.SetupAdjustmentsOf(new CharacterId("vigormortis")));
        Assert.Empty(SectsAndVioletsRoster.SetupAdjustmentsOf(new CharacterId("clockmaker")));
    }

    [Fact]
    public void SetupScriptCarriesPoolsAndAdjustments()
    {
        var script = SectsAndVioletsRoster.AsSetupScript();

        Assert.Equal(13, script.Townsfolk.Count);
        Assert.Equal(4, script.Outsiders.Count);
        Assert.Equal(4, script.Minions.Count);
        Assert.Equal(4, script.Demons.Count);
        Assert.Equal(
            [new SetupAdjustment(CharacterType.Outsider, 1)],
            script.Demons.Single(entry => entry.Character == new CharacterId("fang-gu")).Adjustments);
        Assert.Empty(script.Townsfolk[0].Adjustments);
    }
}
