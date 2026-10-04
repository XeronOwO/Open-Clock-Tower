using OpenClockTower.Application;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 麻脸巫婆票 E15 判定行 3 / 4 / 11 / 15 的残余运行证据（真宿主 + 真 SignalR + 真 SQLite）：
/// 「选在场角色 → 能力照常记已使用且生效」「可以选已死亡玩家」「来源自变后窗口仍有效」
/// 「提交管线补全 PreviousCharacter → R-0029 善良获胜」「含窗口与待定死亡的重启重建、重放不重算」。
/// </summary>
public sealed class PitHagResidueHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 行 3：所选角色已在场 → 无状态变化，但能力照常记「已使用且生效」——
    /// 「已在场则无事发生」不是「能力未生效」（百科《麻脸巫婆》· 角色简介 / 运作方式）。
    /// </summary>
    [Fact]
    public async Task PitHagPicksCharacterInPlay_NoStateChange_ButAbilityUseIsEffective()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "pit-hag"), (2, "clockmaker"), (3, "dreamer"), (4, "no-dashii"), (5, "klutz")),
            "test-pithag-residue-inplay-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-pithag-residue-inplay-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "inplay-1");

        // 第二夜：选「筑梦师」——3 号已经在场，所以这次是什么都不发生，而不是未生效。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-pithag-residue-inplay-night-2");
        Assert.Equal("Accepted", night.Kind);
        var pitHag = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                pitHag.Id.Value,
                "seat:2|dreamer",
                "test-pithag-residue-inplay-answer",
                1L)).Kind);

        var resolved = await WaitForViewAsync(
            storyteller,
            view => view.LastResolution is { } resolution
                && resolution.Seat == 1
                && resolution.Ability == "pit-hag.transform",
            "麻脸巫婆的结算结论没有出现");
        Assert.True(resolved.LastResolution!.Effective, "选在场角色应当照常「生效」，不是「未生效」");
        Assert.Empty(resolved.LastResolution.Malfunctions);

        // 使用账本独立于最新结论：能力已使用且生效。
        Assert.Contains(
            resolved.AbilityUses,
            use => use.Seat == 1 && use.Ability == "pit-hag.transform" && use.Effective);
        Assert.Equal("clockmaker", CharacterOf(host, 2));
    }

    /// <summary>行 4（一）：可以选择已死亡的玩家；角色照变、生死维度不动（百科《重要细节》三-1）。</summary>
    [Fact]
    public async Task PitHagTargetsDeadPlayer_ChangesCharacterOnly()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "pit-hag"), (2, "clockmaker"), (3, "artist"), (4, "no-dashii"), (5, "klutz")),
            "test-pithag-residue-dead-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-pithag-residue-dead-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "dead-1");

        // 说书人先杀死 2 号：已死亡玩家仍然可以被选中。
        var killed = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            2,
            "Dead",
            null,
            null,
            null,
            null,
            "测试：2 号死亡",
            null,
            "test-pithag-residue-dead-report");
        Assert.Equal("Accepted", killed.Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 2));

        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-pithag-residue-dead-night-2");
        Assert.Equal("Accepted", night.Kind);
        var pitHag = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                pitHag.Id.Value,
                "seat:2|sage",
                "test-pithag-residue-dead-answer",
                1L)).Kind);

        await WaitForViewAsync(
            storyteller,
            view => view.Seats.Any(seat => seat.Seat == 2
                && seat.Facts.Any(fact => fact.Dimension == "Character" && fact.Value == "sage")),
            "已死亡的目标没有发生角色变化");
        Assert.Equal("sage", CharacterOf(host, 2));
        Assert.Equal(LifeState.Dead, LifeOf(host, 2));
    }

    /// <summary>
    /// 行 4（二）：创造恶魔后，麻脸巫婆自己失去能力 / 离场，当晚窗口仍有效
    /// （百科《麻脸巫婆》· 规则细节 1；R-0030 第 1 条）。
    /// </summary>
    [Fact]
    public async Task PitHagSourceLosesAbility_WindowStaysOpen()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 0.05, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "pit-hag"), (2, "clockmaker"), (3, "artist"), (4, "no-dashii"), (5, "klutz")),
            "test-pithag-residue-source-assign");
        Assert.Equal("Accepted", assigned.Kind);

        await using var one = await host.ConnectSeatAsync(new SeatId(1));
        var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-pithag-residue-source-night-1");
        Assert.Equal("Accepted", firstNight.Kind);
        await CompleteNightAsync(storyteller, "source-1");

        // 第二夜：把 3 号变成涡流 → 开窗。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-pithag-residue-source-night-2");
        Assert.Equal("Accepted", night.Kind);
        var pitHag = await WaitForRequestAsync(host, new SeatId(1));
        Assert.Equal(
            "Accepted",
            (await one.InvokeAsync<CommandResultDto>(
                "SubmitResponse",
                pitHag.Id.Value,
                "seat:3|vortox",
                "test-pithag-residue-source-transform",
                1L)).Kind);
        var opened = await WaitForViewAsync(
            storyteller,
            view => view.PitHagNight is not null,
            "创造恶魔后窗口没有开");
        Assert.Equal(1, opened.PitHagNight!.Source);

        // 来源自变：说书人上报 1 号不再是麻脸巫婆。
        var changed = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            null,
            "sage",
            null,
            null,
            null,
            "测试：麻脸巫婆离场",
            null,
            "test-pithag-residue-source-change");
        Assert.Equal("Accepted", changed.Kind);
        Assert.Equal("sage", CharacterOf(host, 1));

        // 窗口不随来源自变关闭；追加死亡仍被受理（归因仍记开窗时的麻脸巫婆席位）。
        Assert.NotNull(host.Session.GetStorytellerView().PitHagNight);
        var casualty = await storyteller.InvokeAsync<CommandResultDto>(
            "PitHagCasualty",
            5,
            "来源已离场，窗口仍有效",
            "test-pithag-residue-source-casualty");
        Assert.Equal("Accepted", casualty.Kind);
        Assert.Equal(LifeState.Dead, LifeOf(host, 5));

        // 强推走完夜晚 → 窗口按计划收口。
        await CompleteNightAsync(storyteller, "source-2");
        Assert.Null(host.Session.GetStorytellerView().PitHagNight);
    }

    /// <summary>
    /// 行 11：提交管线用提交前的账补全 <c>PreviousCharacter</c>；胜负求值据此把
    /// 「恶魔 → 非恶魔」判成善良获胜，而不是「从未配置恶魔 → 不判」（R-0029）。
    /// </summary>
    [Fact]
    public async Task CommitPipelineFillsPreviousCharacter_DemonBecomesNonDemon_GoodWins()
    {
        await using var host = new TestServerHost(slotQuotaSeconds: 3600, seatCount: 5, autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();
        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Seats((1, "no-dashii"), (2, "clockmaker"), (3, "artist"), (4, "klutz"), (5, "dreamer")),
            "test-pithag-residue-previous-assign");
        Assert.Equal("Accepted", assigned.Kind);

        // 开一个阶段：胜负求值只在对局开始之后进行（R-0024 第 1 条）。
        var night = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-pithag-residue-previous-night");
        Assert.Equal("Accepted", night.Kind);

        // 产出方（说书人上报）只报新值：变化前角色由提交管线补全。
        var changed = await storyteller.InvokeAsync<CommandResultDto>(
            "ReportSeatState",
            1,
            null,
            "sage",
            null,
            null,
            null,
            "测试：恶魔变成镇民",
            null,
            "test-pithag-residue-previous-report");
        Assert.Equal("Accepted", changed.Kind);

        // 端到端：运行期恶魔角色清零 → 善良获胜。
        var ended = await WaitForViewAsync(
            storyteller,
            view => view.Outcome is not null,
            "恶魔 → 非恶魔没有判出善良获胜");
        Assert.Equal("Good", ended.Outcome!.Winner);
        Assert.Equal("DemonsAllDead", ended.Outcome.Condition);

        // 直接读回事件流：那条角色变化带着补全的「变化前角色」。
        var stored = await host.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var roleChange = stored
            .Select(item => item.Event)
            .OfType<SeatStateChangedEvent>()
            .Single(gameEvent => gameEvent.Seat == new SeatId(1)
                && gameEvent.Character == new CharacterId("sage"));
        Assert.Equal(new CharacterId("no-dashii"), roleChange.PreviousCharacter);
    }

    /// <summary>
    /// 行 15：含窗口与待定死亡的局面经**真重启**从事件流重建——窗口与待定等价、重放不重算、
    /// 重启后仍可继续裁定；玩家重连拿到重建后的挂起请求（D-0010）。
    /// </summary>
    [Fact]
    public async Task WindowWithDeferredSurvivesRestart_AndReplayDoesNotRecompute()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-pithag-residue-{Guid.NewGuid():N}.db");
        long eventsBefore;
        int closesAfter;
        try
        {
            await using (var first = new TestServerHost(
                slotQuotaSeconds: 0.05,
                seatCount: 5,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false))
            {
                await using var storyteller = await first.ConnectStorytellerAsync();
                var assigned = await storyteller.InvokeAsync<CommandResultDto>(
                    "AssignCharacters",
                    Seats((1, "pit-hag"), (2, "clockmaker"), (3, "artist"), (4, "no-dashii"), (5, "klutz")),
                    "test-pithag-residue-restart-assign");
                Assert.Equal("Accepted", assigned.Kind);

                await using var one = await first.ConnectSeatAsync(new SeatId(1));
                await using var three = await first.ConnectSeatAsync(new SeatId(3));
                await using var four = await first.ConnectSeatAsync(new SeatId(4));

                var firstNight = await storyteller.InvokeAsync<CommandResultDto>(
                    "StartNight",
                    1,
                    "Original",
                    "test-pithag-residue-restart-night-1");
                Assert.Equal("Accepted", firstNight.Kind);
                await CompleteNightAsync(storyteller, "restart-1");

                var night = await storyteller.InvokeAsync<CommandResultDto>(
                    "StartNight",
                    2,
                    "Original",
                    "test-pithag-residue-restart-night-2");
                Assert.Equal("Accepted", night.Kind);

                // 创造涡流（开窗 + 激活它的槽位）→ 4 号诺-达鲺击杀 5 号 → 待定死亡。
                var pitHag = await WaitForRequestAsync(first, new SeatId(1));
                Assert.Equal(
                    "Accepted",
                    (await one.InvokeAsync<CommandResultDto>(
                        "SubmitResponse",
                        pitHag.Id.Value,
                        "seat:3|vortox",
                        "test-pithag-residue-restart-transform",
                        1L)).Kind);

                var noDashii = await WaitForRequestAsync(first, new SeatId(4));
                Assert.Equal(
                    "Accepted",
                    (await four.InvokeAsync<CommandResultDto>(
                        "SubmitResponse",
                        noDashii.Id.Value,
                        "seat:5",
                        "test-pithag-residue-restart-kill",
                        1L)).Kind);

                var deferred = await WaitForViewAsync(
                    storyteller,
                    view => view.PitHagNight?.Deferred.Length == 1,
                    "恶魔击杀没有变成待定死亡");
                Assert.Equal(5, deferred.PitHagNight!.Deferred[0].Target);
                Assert.Equal(4, deferred.PitHagNight.Deferred[0].Source);
                closesAfter = deferred.PitHagNight.ClosesAfterSlotIndex;

                // 让被创造的涡流槽位挂起：窗口的关闭点在它之后，机器停住、事件流冻结。
                var vortox = await WaitForRequestAsync(first, new SeatId(3));
                Assert.False(string.IsNullOrWhiteSpace(vortox.Prompt.Context));

                // 冻结节拍再计数：自动推进会在任意一侧宿主里追加配额事件（0.05s 档下先后不定），
                // 让「恢复不追加事件」的计数比较变成竞态。接管模式停掉自动推进后，本用例
                // 只验证重启恢复本身不重算、不追加。
                var freeze = await storyteller.InvokeAsync<CommandResultDto>(
                    "TakeOver",
                    "冻结配额：本用例只验证重启恢复不重算",
                    "test-pithag-residue-restart-freeze");
                Assert.Equal("Accepted", freeze.Kind);

                eventsBefore = (await first.Store.ReadEventsAsync(
                    TestServerHost.GameId,
                    0,
                    CancellationToken.None)).Count;
            }

            // 真重启：同一个数据库，新宿主从事件流恢复。
            await using var restarted = new TestServerHost(
                slotQuotaSeconds: 0.05,
                seatCount: 5,
                databasePath: databasePath,
                deleteDatabaseOnDispose: false,
                autoStartTestNight: false);
            await using var storytellerAfter = await restarted.ConnectStorytellerAsync();

            var restored = await WaitForViewAsync(
                storytellerAfter,
                view => view.PitHagNight is not null,
                "重启后窗口没有恢复");
            Assert.Equal(1, restored.PitHagNight!.Source);
            Assert.Equal(closesAfter, restored.PitHagNight.ClosesAfterSlotIndex);
            var restoredDeferred = Assert.Single(restored.PitHagNight.Deferred);
            Assert.Equal(5, restoredDeferred.Target);
            Assert.Equal(4, restoredDeferred.Source);
            Assert.Equal(LifeState.Alive, LifeOf(restarted, 5));
            Assert.Equal("vortox", CharacterOf(restarted, 3));

            // 重放不重算：恢复不追加任何事件。
            var eventsAfter = await restarted.Store.ReadEventsAsync(
                TestServerHost.GameId,
                0,
                CancellationToken.None);
            Assert.Equal(eventsBefore, eventsAfter.Count);

            // 可继续裁定：窗口内阻止这条待定死亡。
            var prevented = await storytellerAfter.InvokeAsync<CommandResultDto>(
                "ResolveDeferredDeath",
                5,
                false,
                "重启后裁定：免死",
                "test-pithag-residue-restart-prevent");
            Assert.Equal("Accepted", prevented.Kind);
            Assert.Empty(restarted.Session.GetStorytellerView().PitHagNight!.Deferred);
            Assert.Equal(LifeState.Alive, LifeOf(restarted, 5));

            // 玩家重连拿到重建后的挂起请求（被创造的涡流槽位还在等它）。
            Assert.True(
                await TestServerHost.WaitUntilAsync(
                    () => restarted.Session.GetPlayerView(new SeatId(3)).PendingRequest is not null,
                    Wait),
                "重启后玩家重连没有拿到涡流的挂起请求");
        }
        finally
        {
            DeleteFiles(databasePath);
        }
    }

    /// <summary>说书人强推越过剩余槽位（D-0014 兜底）：本组用例只真正结算与残余行有关的那几步。</summary>
    private static async Task CompleteNightAsync(GameClient storyteller, string tag)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var view = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
            if (view.PlanCompleted)
            {
                return;
            }

            var forced = await storyteller.InvokeAsync<CommandResultDto>(
                "ForceAdvance",
                "测试：越过无关槽位",
                $"test-pithag-residue-force-{tag}-{attempt}");
            if (forced.Kind == "Rejected" && forced.RejectionCode == "kernel.PlanAlreadyCompleted")
            {
                // 计划在「查视图」与「强推」之间被自动推进走完（0.05s 配额档下的固有竞态）：
                // 目标已经达成，不算失败。
                return;
            }

            Assert.Equal("Accepted", forced.Kind);
        }

        Assert.Fail("夜晚在 64 次强推内没有走完");
    }

    private static async Task<OperationRequest> WaitForRequestAsync(TestServerHost host, SeatId seat)
    {
        Assert.True(
            await TestServerHost.WaitUntilAsync(
                () => host.Session.GetPlayerView(seat).PendingRequest is not null,
                Wait),
            $"席位 {seat.Value} 没有收到操作请求");

        return host.Session.GetPlayerView(seat).PendingRequest!;
    }

    /// <summary>轮询说书人视图直到条件成立；超时后复查谓词，失败信息带上「在等什么」。</summary>
    private static async Task<StorytellerViewDto> WaitForViewAsync(
        GameClient storyteller,
        Func<StorytellerViewDto, bool> predicate,
        string because)
    {
        var view = await TestServerHost.WaitForViewAsync(storyteller, predicate, Wait);
        Assert.True(view is not null && predicate(view), because);
        return view!;
    }

    private static string? CharacterOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .CharacterValue?.Value;

    private static LifeState? LifeOf(TestServerHost host, int seat) =>
        host.Session.GetStorytellerView().Seats
            .SingleOrDefault(entry => entry.Seat == new SeatId(seat))?
            .LifeValue;

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] seats) =>
        [.. seats.Select(item => new SeatCharacterAssignmentDto
        {
            Seat = item.Seat,
            Character = item.Character,
        })];

    private static void DeleteFiles(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
