using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 麻脸巫婆之夜在真实宿主里的整条链路（口径见 <c>docs/standard/rulings.md</c> R-0030）：
/// 造出恶魔 → 它当夜被唤醒并行动 → 窗口内的恶魔击杀成为**待定死亡** →
/// 说书人裁定（阻止 / 追加死亡）→ 越过恶魔段后窗口收口并拒绝后续命令。
/// </summary>
/// <remarks>
/// 真宿主 + 真 SignalR + 真 SQLite；其他夜晚用 0.05 秒配额自动推进，只真正结算与本票有关的几步。
/// </remarks>
public sealed class PitHagNightHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 1 号麻脸巫婆、4 号诺-达鲺；2 / 3 / 5 号都不在其他夜晚的顺序表上（少几个挂起点）。
    /// </summary>
    [Fact]
    public async Task PitHagCreatesDemon_AdjudicatesDeaths_AndClosesWindow()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "pit-hag" },
                new() { Seat = 2, Character = "clockmaker" },
                new() { Seat = 3, Character = "artist" },
                new() { Seat = 4, Character = "no-dashii" },
                new() { Seat = 5, Character = "klutz" },
            },
            "test-pithag-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        await using var three = await host.ConnectSeatAsync(new SeatId(3));
        await using var four = await host.ConnectSeatAsync(new SeatId(4));

        // 先走完第 1 夜：本局还没开过阶段时首夜必须是第 1 夜，而麻脸巫婆只在**其他夜晚**行动。
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-pithag-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "pithag-1");

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-pithag-night-2");
        Assert.True(
            night.Kind == "Accepted",
            $"StartNight(2) 没被接受：kind={night.Kind} code={night.RejectionCode} msg={night.RejectionMessage} failure={night.Failure}");

        // 麻脸巫婆把 3 号（艺术家）变成**涡流**——一个不在场的恶魔（阵营不变：他仍是善良）。
        var pitHag = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Contains("角色", pitHag.Prompt.Context, StringComparison.Ordinal);
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                pitHag.Id.Value,
                "seat:3|vortox",
                "test-pithag-transform",
                1L)).Kind);

        // 角色变更 + 当夜激活 + 窗口开启。
        var opened = await WaitForViewAsync(
            storyteller,
            view => view.PitHagNight is not null,
            "麻脸巫婆创造恶魔后没有开窗");
        Assert.Equal(1, opened.PitHagNight!.Source);
        Assert.Empty(opened.PitHagNight.Deferred);
        Assert.Equal("vortox", CharacterOf(host, 3));
        Assert.Equal(LifeState.Alive, LifeOf(host, 2));

        // 说书人在窗口内追加死亡：归因为麻脸巫婆。
        var casualty = await storyteller.InvokeAsync<CommandResultDto>(
            "PitHagCasualty",
            2,
            "说书人平衡：让旧线索断掉",
            "test-pithag-casualty-2");
        Assert.Equal("Accepted", casualty.Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 2));

        // 4 号诺-达鲺（原本的恶魔）击杀 5 号 → 窗口内成为待定死亡（不直接致死）。
        var noDashii = await WaitForRequestAsync(host, new SeatId(4));
        Assert.Equal(
            "Accepted",
            (await four.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                noDashii.Id.Value,
                "seat:5",
                "test-pithag-kill-5",
                1L)).Kind);

        var deferred = await WaitForViewAsync(
            storyteller,
            view => view.PitHagNight?.Deferred.Length == 1,
            "恶魔击杀没有变成待定死亡");
        Assert.Equal(5, deferred.PitHagNight!.Deferred[0].Target);
        Assert.Equal(4, deferred.PitHagNight.Deferred[0].Source);
        Assert.Equal(LifeState.Alive, LifeOf(host, 5));

        // 说书人阻止这次死亡（免死）：目标保持存活，窗口继续开着。
        var prevented = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDeferredDeath",
            5,
            false,
            "免死",
            "test-pithag-prevent-5");
        Assert.Equal("Accepted", prevented.Kind);
        Assert.Empty(host.Session.GetStorytellerView().PitHagNight!.Deferred);
        Assert.Equal(LifeState.Alive, LifeOf(host, 5));

        // 当夜被创造的涡流（善良恶魔）同样在这一夜行动：它击杀 4 号，待定，随后按默认结果生效。
        var vortox = await WaitForRequestAsync(host, new SeatId(3));
        Assert.Equal(
            "Accepted",
            (await three.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                vortox.Id.Value,
                "seat:4",
                "test-pithag-kill-4",
                1L)).Kind);

        // 越过最后一个恶魔槽位 → 窗口收口；未裁定的待定死亡按恶魔攻击的自然结果生效。
        var closed = await WaitForViewAsync(
            storyteller,
            view => view.PitHagNight is null,
            "窗口没有在恶魔段之后收口");
        Assert.Null(closed.PitHagNight);
        Assert.Equal(LifeState.Dead, LifeOf(host, 4));

        // 收口之后两条命令都被显式拒绝（不是静默忽略）。
        var late = await storyteller.InvokeAsync<CommandResultDto>(
            "PitHagCasualty",
            5,
            "太晚了",
            "test-pithag-late-casualty");
        Assert.Equal("Rejected", late.Kind);
        Assert.Equal("kernel.NoPitHagNight", late.RejectionCode);
    }

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本用例只真正结算与本票有关的那几步。</summary>
    private static async Task CompleteNightAsync(GameClient storyteller, string tag)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过无关槽位",
                $"test-pithag-force-{tag}-{attempt}");
            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 24 次强推内没有走完");
    }

    private static string? CharacterOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .CharacterValue?.Value;

    private static LifeState? LifeOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .LifeValue;

    private static async Task<OperationRequest> WaitForRequestAsync(TestServerHost host, SeatId seat)
    {
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(seat).PendingRequest is not null,
                Wait),
            $"席位 {seat.Value} 没有收到操作请求");

        return host.Session.GetPlayerView(seat).PendingRequest!;
    }

    private static async Task<StorytellerViewDto> WaitForViewAsync(
        GameClient storyteller,
        Func<StorytellerViewDto, bool> predicate,
        string because)
    {
        var view = await TestServerHost.WaitForViewAsync(storyteller, predicate, Wait);
        Assert.True(view is not null, because);
        return view!;
    }
}
