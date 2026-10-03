using System.Text.Json;
using OpenClockTower.Kernel;

namespace OpenClockTower.Kernel.Tests;

/// <summary>
/// 白天事实的**动作时刻快照**（<c>docs/standard/rulings.md</c> R-0037）：提名 / 投票事件带
/// 「动作发生时的角色」，折叠进白天账，并且必须进步骤机结构比较器——漏比会让重建校验
/// 在「谁举过手」上失明。
/// </summary>
public sealed class DayFactSnapshotTests
{
    /// <summary>提名事件与白天账都要留下提名者的角色快照（城镇公告员按它推演）。</summary>
    [Fact]
    public void Nominate_CarriesAndFoldsNominatorCharacter()
    {
        var context = Context((1, LifeState.Alive, "witch"), (2, LifeState.Alive, "clockmaker"));
        var state = DayPhaseFixture.StartDay();

        var outcome = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2);

        var made = Assert.Single(outcome.Events.OfType<NominationMadeEvent>());
        Assert.Equal(new CharacterId("witch"), made.NominatorCharacter);

        var nomination = Assert.Single(outcome.State.Day!.OpenDay!.Nominations);
        Assert.Equal(new CharacterId("witch"), nomination.NominatorCharacter);
    }

    /// <summary>投票动作按发生顺序进表：撤回也是一条事实（R-0037 第 2 条）。</summary>
    [Fact]
    public void CastVote_FoldsEveryAttempt_WithCharacterSnapshot()
    {
        var context = Context((1, LifeState.Alive, "no-dashii"), (2, LifeState.Alive, "clockmaker"));
        var state = DayPhaseFixture.StartDay();
        var opened = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2).State;

        var voted = DayPhaseFixture.Vote(opened, context, voter: 1, index: 1, voted: true);
        var cast = Assert.Single(voted.Events.OfType<VoteCastEvent>());
        Assert.Equal(new CharacterId("no-dashii"), cast.VoterCharacter);

        var withdrawn = DayPhaseFixture.Vote(voted.State, context, voter: 1, index: 1, voted: false);

        var attempts = withdrawn.State.Day!.OpenDay!.VoteAttempts;
        Assert.Equal(2, attempts.Count);
        Assert.Equal(new DayVoteAttempt
        {
            NominationIndex = 1,
            Voter = new SeatId(1),
            VoterCharacter = new CharacterId("no-dashii"),
            Voted = true,
        }, attempts[0]);
        Assert.False(attempts[1].Voted);
        Assert.Empty(withdrawn.State.Day!.OpenDay!.OpenNomination!.Ballot);
    }

    /// <summary>角色维度未观测：快照记 null，不把「不知道」写成「不是恶魔」（不猜）。</summary>
    [Fact]
    public void CastVote_WithoutCharacterObservation_KeepsNullSnapshot()
    {
        var context = Context((1, LifeState.Alive, null), (2, LifeState.Alive, null));
        var state = DayPhaseFixture.StartDay();
        var opened = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2).State;

        var outcome = DayPhaseFixture.Vote(opened, context, voter: 1, index: 1, voted: true);

        var cast = Assert.Single(outcome.Events.OfType<VoteCastEvent>());
        Assert.Null(cast.VoterCharacter);
        Assert.Null(outcome.State.Day!.OpenDay!.VoteAttempts[0].VoterCharacter);
    }

    /// <summary>快照与动作表都进结构比较器：票面相同的两份账，动作表不同就不等价。</summary>
    [Fact]
    public void Comparer_SeesSnapshotsAndAttempts()
    {
        var context = Context((1, LifeState.Alive, "no-dashii"), (2, LifeState.Alive, "clockmaker"));
        var state = DayPhaseFixture.StartDay();
        var opened = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2).State;
        var voted = DayPhaseFixture.Vote(opened, context, voter: 1, index: 1, voted: true).State;

        Assert.True(StepMachineStateComparer.AreEquivalent(voted, voted with { }));

        var day = voted.Day!.Days[0];
        var tamperedAttempt = voted with
        {
            Day = voted.Day! with
            {
                Days =
                [
                    day with
                    {
                        VoteAttempts = [day.VoteAttempts[0] with { VoterCharacter = new CharacterId("clockmaker") }],
                    },
                ],
            },
        };
        Assert.False(StepMachineStateComparer.AreEquivalent(voted, tamperedAttempt));

        var tamperedNominator = voted with
        {
            Day = voted.Day! with
            {
                Days = [day with { Nominations = [day.Nominations[0] with { NominatorCharacter = null }] }],
            },
        };
        Assert.False(StepMachineStateComparer.AreEquivalent(voted, tamperedNominator));
    }

    /// <summary>
    /// 新字段要能过 JSON 往返（快照落库与事件载荷走的就是这条管道，`JsonSerializerDefaults.Web`）。
    /// </summary>
    [Fact]
    public void DayLedger_RoundTripsThroughJson()
    {
        var context = Context((1, LifeState.Alive, "no-dashii"), (2, LifeState.Alive, "clockmaker"));
        var state = DayPhaseFixture.StartDay();
        var opened = DayPhaseFixture.Nominate(state, context, nominator: 1, nominee: 2).State;
        var voted = DayPhaseFixture.Vote(opened, context, voter: 1, index: 1, voted: true).State;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var payload = JsonSerializer.Serialize(voted.Day, options);
        var restored = JsonSerializer.Deserialize<DayState>(payload, options);

        Assert.NotNull(restored);
        Assert.Equal(payload, JsonSerializer.Serialize(restored, options));
        Assert.Equal(new CharacterId("no-dashii"), restored!.Days[0].Nominations[0].NominatorCharacter);
        Assert.Equal(new CharacterId("no-dashii"), restored.Days[0].VoteAttempts[0].VoterCharacter);
    }

    /// <summary>按「席位 + 生死 + 可选角色」构造状态账；角色为 null 表示该维度未观测。</summary>
    private static SettlementContext Context(params (int Seat, LifeState Life, string? Character)[] seats)
    {
        var events = seats
            .Select(item => (GameEvent)new SeatStateChangedEvent
            {
                Seat = new SeatId(item.Seat),
                Life = item.Life,
                Character = item.Character is { } slug ? new CharacterId(slug) : null,
                Reason = "test.day-fact-snapshot",
            })
            .ToArray();

        return new SettlementContext
        {
            State = GameStateMachine.Fold(events),
            Seats = [.. seats.Select(item => new SeatId(item.Seat)).OrderBy(seat => seat.Value)],
            Abilities = NoAbilities.Instance,
        };
    }

    private sealed class NoAbilities : IAbilityResolutionCatalog
    {
        internal static readonly NoAbilities Instance = new();

        public IAbilityResolution? Find(CharacterId character) => null;
    }
}
