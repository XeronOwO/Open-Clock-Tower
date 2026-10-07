using System.Collections.Concurrent;
using System.Text.Json;
using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 玩家端「本人角色」出口的整条链路（口径见 <c>docs/standard/rulings.md</c> R-0059）：
/// 本人看得到自己的角色与阵营、**无关席位一个都看不到**、换角后定向推送即时跟随、重连按同一份事实补齐。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite。反方向（验收矩阵「不该看见的人确实没看见」）用序列化后的玩家 DTO
/// 断言：别人那一格的角色既不在本人视图的字段里，也不在任何一条推给本人的载荷里。
/// </remarks>
public sealed class PlayerOwnCharacterHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>本局分配：1 钟表匠 / 2 筑梦师 / 3 艺术家 / 4 诺-达鲺（邪恶）/ 5 呆瓜。</summary>
    private static readonly (int Seat, string Character)[] Setup =
    [
        (1, "clockmaker"),
        (2, "dreamer"),
        (3, "artist"),
        (4, "no-dashii"),
        (5, "klutz"),
    ];

    /// <summary>矩阵行 1 + 行 2：分配之后本人看得到自己的角色与阵营；别人那一格在 wire 上一个值都没有。</summary>
    [Fact]
    public async Task Assignment_OwnSeatSeesOwnCharacter_AndNoOtherSeatCharacterRidesAlong()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats(),
            "test-own-character-assign");
        Assert.Equal("Accepted", assigned.Kind);

        foreach (var (seat, character) in Setup)
        {
            var view = host.Session.GetPlayerView(new SeatId(seat));
            var expected = character == "no-dashii" ? Alignment.Evil : Alignment.Good;
            Assert.Equal(character, view.Character?.Value);
            Assert.Equal(expected, view.Alignment);

            // 反方向：把这一份投影序列化（wire 形状），里面出现的角色值**只有自己那一个**。
            var values = StringValuesOf(JsonSerializer.Serialize(ProjectionMapper.ToDto(view)));
            Assert.Contains(character, values);
            foreach (var other in Setup.Where(row => row.Seat != seat))
            {
                Assert.DoesNotContain(other.Character, values);
            }
        }
    }

    /// <summary>矩阵行 1 的前置：还没分配时本人视图里是 null——不猜、不给默认角色（D-0015）。</summary>
    [Fact]
    public async Task BeforeAssignment_OwnCharacterIsNull_NotGuessed()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 3, autoStartTestNight: false);

        var view = host.Session.GetPlayerView(new SeatId(1));
        Assert.Null(view.Character);
        Assert.Null(view.Alignment);

        // wire 形状里也不许冒出任何一个花名册角色值（"没观测到"不是"随便挑一个"）。
        var values = StringValuesOf(JsonSerializer.Serialize(ProjectionMapper.ToDto(view)));
        Assert.DoesNotContain(values, value => Setup.Any(row => row.Character == value));
    }

    /// <summary>
    /// 矩阵行 3（推送面）+ 行 1（重连）：换角 / 换阵营之后**只有本人**收到一份定向的自己视图，
    /// 别的席位一条都收不到；重连（重新 JoinSeat）拿到的是同一份事实。
    /// </summary>
    [Fact]
    public async Task CharacterChange_PushesOwnViewToThatSeatOnly_AndReconnectCarriesIt()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var own = new ConcurrentQueue<PlayerViewDto>();
        var bystander = new ConcurrentQueue<PlayerViewDto>();
        await using var one = await host.ConnectSeatAsync(
            new SeatId(1),
            onPlayerViewChanged: (_, view) => own.Enqueue(view));
        await using var two = await host.ConnectSeatAsync(
            new SeatId(2),
            onPlayerViewChanged: (_, view) => bystander.Enqueue(view));

        // 分配本身就是一条角色变化（R-0023 在同一条事件里补阵营）：本人视图应当立刻到。
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats(),
            "test-own-character-push-assign");
        Assert.Equal("Accepted", assigned.Kind);
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => own.Any(view => view.Character == "clockmaker" && view.Alignment == "Good"),
                Wait),
            $"席位 1 没有收到带本人角色的视图推送（最后：{own.LastOrDefault()?.Character ?? "无"}）");

        // 说书人上报换角 + 换阵营：1 号变成邪恶的贤者。
        // 先清掉观察队列：入座 = 认领席位，服务端会推一条"席位名变了"的整视图（D-0021），
        // 那一条发生在开局分配**之前**、角色本来就是空的——它不属于这条用例问的"换角会不会泄露"。
        // 不清的话反方向断言会把"还没分配角色"误判成"换角串台"（本轮实测踩到）。
        bystander.Clear();
        var reported = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            null,
            "sage",
            "Evil",
            null,
            null,
            "测试：上报换角换阵营",
            null,
            "test-own-character-report");
        Assert.Equal("Accepted", reported.Kind);

        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => own.Any(view => view.Character == "sage" && view.Alignment == "Evil"),
                Wait),
            $"席位 1 没有收到换角后的本人视图（最后：{own.LastOrDefault()?.Character ?? "无"}）");

        // 让 2 号也拿到至少一条推送（阶段边界是全体推送），反方向断言才有对象——空集合上的
        // "不含"是假绿（闸门写在断言里，不靠"应该不会有"）。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-own-character-night");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage}");
        Assert.True(
            await TestServerHost.WaitUntilAsync(() => !bystander.IsEmpty, Wait),
            "席位 2 一条视图推送都没收到，反方向断言会变成空转");

        // 反方向：推给 2 号的每一条载荷里，角色永远只有他自己那一个（换角既没泄露、也没串台）。
        Assert.All(bystander, view =>
        {
            Assert.Equal("dreamer", view.Character);
            Assert.Equal("Good", view.Alignment);
        });

        // 重连 = 快照 + 从该序号起的事件（D-0010 / D-0012）：本人角色按同一份事实补齐，不做本地跟随。
        var bundle = await host.Session.GetReconnectBundleAsync(new SeatId(1), 0, CancellationToken.None);
        Assert.Equal("sage", bundle.View.Character?.Value);
        Assert.Equal(Alignment.Evil, bundle.View.Alignment);

        // 顺带锁住"两个维度独立"：换成贤者不会顺手改别人的账（2 号仍是自己的角色与阵营）。
        Assert.Equal("dreamer", host.Session.GetPlayerView(new SeatId(2)).Character?.Value);
        Assert.Equal(Alignment.Good, host.Session.GetPlayerView(new SeatId(2)).Alignment);
    }

    /// <summary>席位与角色名称的分配请求形状。</summary>
    private static SeatCharacterAssignmentDto[] Seats() =>
        [.. Setup.Select(row => new SeatCharacterAssignmentDto { Seat = row.Seat, Character = row.Character })];

    /// <summary>
    /// 把一份 JSON 里所有字符串**值**摊平。
    /// </summary>
    /// <remarks>
    /// 只看值、不看字段名：字段名里带 `Artist`（`canAskArtistQuestion`）这类词会让整串匹配误伤，
    /// 而"别人那一格的角色能不能被读到"问的正是值。
    /// </remarks>
    private static List<string> StringValuesOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        var values = new List<string>();
        Collect(document.RootElement, values);
        return values;
    }

    /// <summary>递归收集 JSON 里的字符串值。</summary>
    private static void Collect(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, values);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Collect(property.Value, values);
                }

                break;
        }
    }
}
