using OpenClockTower.Application;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 席位名读模型（D-0021）：装载 / 登记 / 移除 / 改名，快照按席位升序、无名字的席位不出现。
/// </summary>
public sealed class SeatNameDirectoryTests
{
    private static readonly GameId Game = new("directory-test-game");

    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.FromHours(8));

    /// <summary>全量装载：按绑定映射名字；账号已不存在 / 没名字的席位不出现。</summary>
    [Fact]
    public void Load_KeepsOnlySeatsWithKnownNames_AndOrdersBySeat()
    {
        var directory = new SeatNameDirectory();
        directory.Load(
            [
                Binding(2, 20),
                Binding(1, 10),
                Binding(3, 30),
            ],
            new Dictionary<AccountId, string>
            {
                [new AccountId(10)] = "爱丽丝",
                [new AccountId(20)] = "鲍勃",
                // 30 号账号已不存在：席位 3 不出现。
            });

        var snapshot = directory.Snapshot();

        Assert.Equal([new SeatId(1), new SeatId(2)], snapshot.Select(item => item.Seat));
        Assert.Equal(["爱丽丝", "鲍勃"], snapshot.Select(item => item.DisplayName));
        Assert.Null(directory.NameOf(new SeatId(3)));
        Assert.Equal("鲍勃", directory.NameOf(new SeatId(2)));
    }

    /// <summary>登记 / 移除：认领后出现，解除后消失。</summary>
    [Fact]
    public void SetAndRemove_TrackTheBinding()
    {
        var directory = new SeatNameDirectory();

        directory.Set(new SeatId(4), new AccountId(1), "小明");
        Assert.Equal("小明", directory.NameOf(new SeatId(4)));

        directory.Remove(new SeatId(4));
        Assert.Null(directory.NameOf(new SeatId(4)));
        Assert.Empty(directory.Snapshot());
    }

    /// <summary>账号改名：该账号在本局的全部席位一起更新（改名即时生效，D-0021）。</summary>
    [Fact]
    public void Rename_UpdatesEverySeatOfThatAccount()
    {
        var directory = new SeatNameDirectory();
        directory.Load(
            [Binding(1, 10), Binding(2, 10), Binding(3, 20)],
            new Dictionary<AccountId, string> { [new AccountId(10)] = "旧名", [new AccountId(20)] = "别人" });

        directory.Rename(new AccountId(10), "新名");

        Assert.Equal("新名", directory.NameOf(new SeatId(1)));
        Assert.Equal("新名", directory.NameOf(new SeatId(2)));
        Assert.Equal("别人", directory.NameOf(new SeatId(3)));
    }

    private static SeatBinding Binding(int seat, int accountId) => new()
    {
        GameId = Game,
        Seat = new SeatId(seat),
        AccountId = new AccountId(accountId),
        BoundAt = Now,
    };
}
