using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 建表在真实宿主里的行为：开局分配（角色 + 初始生死）→ 说书人开夜 →
/// 计划按顺序推动操作请求与裁定点；重启后分配与计划都还在。
/// </summary>
/// <remarks>
/// 依据 D-0013 §1（按剧本完整顺序表建表、空槽位照样走配额）、D-0017（分配进事件流）
/// 与 R-0014（口径是引擎输入、记录在计划里）。跑的是真宿主 + 真 SignalR + 真 SQLite。
/// </remarks>
public sealed class NightBuildHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>四个席位：1 号筑梦师 + 三个没有夜晚行动的角色（不会因缺契约而拒绝开夜）。</summary>
    private static SeatCharacterAssignmentDto[] DreamerFirstAssignments() =>
    [
        new() { Seat = 1, Character = "dreamer" },
        new() { Seat = 2, Character = "artist" },
        new() { Seat = 3, Character = "klutz" },
        new() { Seat = 4, Character = "mutant" },
    ];

    /// <summary>开局分配 → 开首夜（原本口径）→ 筑梦师槽位发出真实请求，选项来自局内席位。</summary>
    [Fact]
    public async Task AssignedDreamer_ReceivesRealOperationRequest()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 4,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            DreamerFirstAssignments(),
            "test-build-assign-dreamer");
        Assert.Equal("Accepted", assigned.Kind);

        // 分配进状态账：角色与初始生死都在，且带固定原因（R-0015）。
        var beforeNight = await storyteller.InvokeAsync<StorytellerViewDto>("GetStorytellerView");
        var seatOne = Assert.Single(beforeNight.Seats, entry => entry.Seat == 1);
        var character = Assert.Single(seatOne.Facts, fact => fact.Dimension == "Character");
        Assert.Equal("dreamer", character.Value);
        Assert.Equal("setup.assignment", character.Reason);
        var life = Assert.Single(seatOne.Facts, fact => fact.Dimension == "Life");
        Assert.Equal("Alive", life.Value);

        OperationRequestDto? received = null;
        await using var seatOneConnection = await host.ConnectSeatAsync(new SeatId(1), request => received = request);

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-build-start-dreamer");
        Assert.Equal("Accepted", started.Kind);

        var requested = await TestServerHost.WaitUntilAsync(() => received is not null, Wait);
        Assert.True(requested, "筑梦师席位在超时前没有收到操作请求");

        Assert.Equal(1, received!.Seat);
        Assert.Contains("筑梦师", received.Context, StringComparison.Ordinal);
        Assert.Equal(new[] { "seat:2", "seat:3", "seat:4" }, received.Options.Select(option => option.Value));
    }

    /// <summary>钟表匠槽位不产生玩家请求，而是给说书人一个裁定点（信息由说书人给，D-0002）。</summary>
    [Fact]
    public async Task AssignedClockmaker_RaisesStorytellerDecisionPoint()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 0.05,
            seatCount: 3,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var assigned = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "clockmaker" },
                new() { Seat = 2, Character = "artist" },
                new() { Seat = 3, Character = "klutz" },
            },
            "test-build-assign-clockmaker");
        Assert.Equal("Accepted", assigned.Kind);

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-build-start-clockmaker");
        Assert.Equal("Accepted", started.Kind);

        var view = await TestServerHost.WaitForViewAsync(
            storyteller,
            candidate => candidate.AwaitingDecisionId is not null,
            Wait);

        Assert.NotNull(view);
        Assert.Equal("clockmaker", view!.CurrentSlotId);
        Assert.Contains("clockmaker", Assert.IsType<string>(view.AwaitingDecisionId), StringComparison.Ordinal);
        Assert.Contains("钟表匠", view.CurrentSlotContext, StringComparison.Ordinal);
    }

    /// <summary>非法分配与非法开夜全部走显式拒绝，且拒绝后不改状态。</summary>
    [Fact]
    public async Task IllegalAssignmentsAndStartNight_AreRejected()
    {
        await using var host = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            autoStartTestNight: false);
        await using var storyteller = await host.ConnectStorytellerAsync();

        var unknownSeat = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new[] { new SeatCharacterAssignmentDto { Seat = 9, Character = "dreamer" } },
            "test-build-bad-seat");
        Assert.Equal("Rejected", unknownSeat.Kind);
        Assert.Equal("legality.seat_unknown", unknownSeat.RejectionCode);

        var unknownCharacter = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new[] { new SeatCharacterAssignmentDto { Seat = 1, Character = "not-a-role" } },
            "test-build-bad-character");
        Assert.Equal("Rejected", unknownCharacter.Kind);
        Assert.Equal("legality.character_unknown", unknownCharacter.RejectionCode);

        var duplicated = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 1, Character = "dreamer" },
                new() { Seat = 2, Character = "dreamer" },
            },
            "test-build-duplicated-character");
        Assert.Equal("Rejected", duplicated.Kind);
        Assert.Equal("legality.character_duplicated", duplicated.RejectionCode);

        var empty = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            Array.Empty<SeatCharacterAssignmentDto>(),
            "test-build-empty-assignment");
        Assert.Equal("Rejected", empty.Kind);
        Assert.Equal("legality.assignment_empty", empty.RejectionCode);

        // 没有开过任何阶段：第一夜必须是第 1 夜。
        var secondNightFirst = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            2,
            "Original",
            "test-build-night-2-first");
        Assert.Equal("Rejected", secondNightFirst.Kind);
        Assert.Equal("legality.first_night_must_be_one", secondNightFirst.RejectionCode);

        // 只分配一半就开夜：建表器拒绝（"不在场"与"忘了分配"必须区分得开）。
        var partial = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new[] { new SeatCharacterAssignmentDto { Seat = 1, Character = "dreamer" } },
            "test-build-partial-assignment");
        Assert.Equal("Accepted", partial.Kind);

        var incomplete = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-build-incomplete");
        Assert.Equal("Rejected", incomplete.Kind);
        Assert.Equal("legality.plan.seat_unassigned", incomplete.RejectionCode);

        // 补齐席位后开夜成功；开了阶段以后不再允许改分配。
        var completed = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new SeatCharacterAssignmentDto[]
            {
                new() { Seat = 2, Character = "artist" },
                new() { Seat = 3, Character = "klutz" },
            },
            "test-build-complete-assignment");
        Assert.Equal("Accepted", completed.Kind);

        var started = await storyteller.InvokeAsync<CommandResultDto>(
            "StartNight",
            1,
            "Original",
            "test-build-start");
        Assert.Equal("Accepted", started.Kind);

        var reassign = await storyteller.InvokeAsync<CommandResultDto>(
            "AssignCharacters",
            new[] { new SeatCharacterAssignmentDto { Seat = 1, Character = "clockmaker" } },
            "test-build-reassign-after-start");
        Assert.Equal("Rejected", reassign.Kind);
        Assert.Equal("phase.already_started", reassign.RejectionCode);
    }

    /// <summary>分配与计划都在事件流里：重启后按重放恢复（D-0010），口径记录留在计划上（R-0014）。</summary>
    [Fact]
    public async Task AssignmentAndNightPlan_SurviveHostRestart()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-night-build-{Guid.NewGuid():N}.db");

        await using (var first = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            deleteDatabaseOnDispose: false,
            autoStartTestNight: false))
        {
            await using var storyteller = await first.ConnectStorytellerAsync();

            var assigned = await storyteller.InvokeAsync<CommandResultDto>(
                "AssignCharacters",
                new SeatCharacterAssignmentDto[]
                {
                    new() { Seat = 1, Character = "dreamer" },
                    new() { Seat = 2, Character = "artist" },
                    new() { Seat = 3, Character = "klutz" },
                },
                "test-build-restart-assign");
            Assert.Equal("Accepted", assigned.Kind);

            var started = await storyteller.InvokeAsync<CommandResultDto>(
                "StartNight",
                1,
                "Original",
                "test-build-restart-start");
            Assert.Equal("Accepted", started.Kind);
        }

        await using var restarted = new TestServerHost(
            slotQuotaSeconds: 3600,
            seatCount: 3,
            databasePath: databasePath,
            autoStartTestNight: false);

        await using var storytellerAfterRestart = await restarted.ConnectStorytellerAsync();
        var view = await TestServerHost.WaitForViewAsync(
            storytellerAfterRestart,
            candidate => candidate.Phase == "FirstNight",
            Wait);

        Assert.NotNull(view);
        Assert.Equal(13, view!.SlotCount); // 首夜原本口径的槽位数
        Assert.Equal("dusk", view.CurrentSlotId);
        var seat = Assert.Single(view.Seats, entry => entry.Seat == 1);
        Assert.Contains(seat.Facts, fact => fact.Dimension == "Character" && fact.Value == "dreamer");
        Assert.Contains(seat.Facts, fact => fact.Dimension == "Life" && fact.Value == "Alive");

        // 事件流本身可核：分配事实带初始生死与固定原因；计划里记着本局实际口径。
        var events = await restarted.Store.ReadEventsAsync(TestServerHost.GameId, 0, CancellationToken.None);
        var assignmentEvents = events
            .Select(stored => stored.Event)
            .OfType<SeatStateChangedEvent>()
            .Where(changed => changed.Character is not null)
            .ToArray();
        Assert.Equal(3, assignmentEvents.Length);
        Assert.All(assignmentEvents, changed => Assert.Equal(LifeState.Alive, changed.Life));
        Assert.All(assignmentEvents, changed => Assert.Equal("setup.assignment", changed.Reason));

        var phaseStarted = Assert.Single(events.Select(stored => stored.Event).OfType<PhaseStartedEvent>());
        Assert.Equal("Original", phaseStarted.Plan.Variant);
        Assert.Equal("sv:night-1", phaseStarted.Plan.Label);
    }
}
