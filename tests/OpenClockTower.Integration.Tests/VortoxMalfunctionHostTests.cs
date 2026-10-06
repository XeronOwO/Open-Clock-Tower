using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// R-0004 涡流路径在真宿主上的行为：涡流存活时镇民信息能力**照常生效**，但留下 `Vortox` 失效记录；
/// 记录只说书人可见，且随事件流重放保留（不重算）。
/// </summary>
/// <remarks>
/// 跑真宿主 + 真 SignalR + 真 SQLite；席位里不含会占「玩家选择槽」的角色（那会阻塞推进）。
/// </remarks>
public sealed class VortoxMalfunctionHostTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task InfoAbilityUnderLivingVortox_LeavesStorytellerOnlyRecord()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"oct-vortox-ledger-{Guid.NewGuid():N}.db");
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
                    Seats(
                        (1, "vortox"),
                        (2, "clockmaker"),
                        (3, "dreamer"),
                        (4, "klutz"),
                        (5, "mutant")),
                    "test-vortox-ledger-assign");
                Assert.Equal("Accepted", assigned.Kind);

                var started = await storyteller.InvokeAsync<CommandResultDto>(
                    "StartNight",
                    1,
                    "Original",
                    "test-vortox-ledger-night-1");
                Assert.Equal("Accepted", started.Kind);

                // 钟表匠是入口裁定点：涡流在场时信息必须为假（R-0028），但能力本身照常生效。
                var clockmakerDecision = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.AwaitingDecisionId is not null && view.CurrentSlotId == "clockmaker",
                    Wait);
                Assert.NotNull(clockmakerDecision);
                Assert.Equal("clockmaker", clockmakerDecision!.CurrentSlotId);
                Assert.NotNull(clockmakerDecision.AwaitingDecisionId);
                Assert.Contains("涡流", clockmakerDecision.AwaitingDecisionContext, StringComparison.Ordinal);
                await ResolveDecisionAsync(
                    storyteller,
                    clockmakerDecision,
                    "距离 2。",
                    "test-vortox-ledger-clockmaker");

                var settled = await TestServerHost.WaitForViewAsync(
                    storyteller,
                    view => view.LastResolution is { Ability: "clockmaker" },
                    Wait);
                Assert.NotNull(settled);
                Assert.True(settled!.LastResolution!.Effective, "涡流不改「能力是否生效」——它让信息必须为假");
                Assert.Equal(new[] { "Vortox" }, settled.LastResolution.Malfunctions);
                Assert.Contains(
                    settled.Malfunctions,
                    malfunction => malfunction.Seat == 2
                        && malfunction.Ability == "clockmaker"
                        && malfunction.Kind == "Vortox");

                // 失效归因只说书人可见：玩家投影里没有失效面（R-0004 / D-0012）。
                var playerView = JsonSerializer.Serialize(first.Session.GetPlayerView(new SeatId(2)));
                Assert.DoesNotContain("malfunction", playerView, StringComparison.OrdinalIgnoreCase);
            }

            // 重放：失效记录随事件流恢复，不重算。
            await using var restarted = new TestServerHost(
                slotQuotaSeconds: 3600,
                seatCount: 5,
                databasePath: databasePath,
                autoStartTestNight: false);
            await using var storytellerAfterRestart = await restarted.ConnectStorytellerAsync();
            var restored = await TestServerHost.WaitForViewAsync(
                storytellerAfterRestart,
                view => view.Malfunctions.Length > 0,
                Wait);
            Assert.NotNull(restored);
            Assert.Contains(
                restored!.Malfunctions,
                malfunction => malfunction.Seat == 2
                    && malfunction.Ability == "clockmaker"
                    && malfunction.Kind == "Vortox");
        }
        finally
        {
            DeleteDatabaseFiles(databasePath);
        }
    }

    private static void DeleteDatabaseFiles(string databasePath)
    {
        TestDatabaseFiles.Delete(databasePath);
    }

    private static async Task ResolveDecisionAsync(
        GameClient storyteller,
        StorytellerViewDto view,
        string decision,
        string key)
    {
        var resolved = await storyteller.InvokeAsync<CommandResultDto>(
            "ResolveDecisionPoint",
            view.AwaitingDecisionId,
            decision,
            null,
            key);
        Assert.Equal("Accepted", resolved.Kind);
    }

    private static SeatCharacterAssignmentDto[] Seats(params (int Seat, string Character)[] seats) =>
        [.. seats.Select(item => new SeatCharacterAssignmentDto
        {
            Seat = item.Seat,
            Character = item.Character,
        })];
}
