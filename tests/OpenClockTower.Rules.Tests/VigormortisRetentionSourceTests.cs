using OpenClockTower.Kernel;
using OpenClockTower.Rules;

namespace OpenClockTower.Rules.Tests;

/// <summary>
/// 亡骨魔常驻来源的规则回归：被杀死爪牙身上落「保留能力」窗口、他**选定那一侧**最近的镇民中毒
/// （跳过非镇民、死亡也算、随座次与角色变化重算）、亡骨魔死亡 / 离场 / 换角 / 爪牙不再是爪牙时
/// 两条期望一起消失、标记移除后不复原、输入不齐时判定不了（不猜）。
/// </summary>
/// <remarks>
/// 来源：百科《亡骨魔》· 2026-10-01 抓取 · 规则细节 13 / 14 / 19–24；平台口径见
/// <c>docs/standard/rulings.md</c> R-0056。
/// </remarks>
public sealed class VigormortisRetentionSourceTests
{
    private static readonly VigormortisRetentionSource Source = new();

    /// <summary>正常路径：窗口落在被杀的爪牙身上，中毒落在说书人选的那一侧最近的镇民身上。</summary>
    [Fact]
    public void ValidKill_ExpectsWindowAndPoison()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));

        Assert.True(assessment.IsConclusive);
        Assert.Equal(2, assessment.Expectations.Count);

        var window = Assert.Single(assessment.Expectations, item => item.Window is not null);
        Assert.Equal(new EffectId("standing:vigormortis.retention:1:2"), window.Id);
        Assert.Equal(new SeatId(2), window.Target);
        Assert.Equal(EffectWindowKind.RetainedAbility, window.Window);
        Assert.Equal(new SeatId(1), window.Source);
        Assert.Equal(new CharacterId("vigormortis"), window.SourceCharacter);
        Assert.Null(window.Dimension);

        var poison = Assert.Single(assessment.Expectations, item => item.Dimension is not null);
        Assert.Equal(new SeatId(3), poison.Target);
        Assert.Equal(EffectDimension.Poison, poison.Dimension);
        Assert.Null(poison.Window);
    }

    /// <summary>说书人选逆时针：中毒落在另一侧最近的镇民身上（跳过恶魔与外来者）。</summary>
    [Fact]
    public void CounterClockwiseSide_SkipsNonTownsfolk()
    {
        var assessment = Source.Evaluate(Context(
            [
                (1, "vigormortis", LifeState.Alive),
                (2, "witch", LifeState.Dead),
                (3, "clockmaker", LifeState.Alive),
                (4, "dreamer", LifeState.Alive),
                (5, "mutant", LifeState.Alive),
            ],
            [new Kill(1, 2, SeatRingDirection.CounterClockwise)]));

        var poison = Assert.Single(assessment.Expectations, item => item.Dimension is not null);
        Assert.Equal(new SeatId(4), poison.Target);
    }

    /// <summary>中毒的镇民已经死亡也照中毒（规则细节 3：「即使该镇民已经死亡」）。</summary>
    [Fact]
    public void DeadTownsfolkStillCounts()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Dead)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));

        var poison = Assert.Single(assessment.Expectations, item => item.Dimension is not null);
        Assert.Equal(new SeatId(3), poison.Target);
    }

    /// <summary>说书人没选侧（场上没有镇民）：只有保留下能力的期望。</summary>
    [Fact]
    public void NoSide_ExpectsWindowOnly()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "cerenovus", LifeState.Alive)],
            [new Kill(1, 2, null)]));

        Assert.True(assessment.IsConclusive);
        Assert.Single(assessment.Expectations);
        Assert.Equal(EffectWindowKind.RetainedAbility, assessment.Expectations[0].Window);
    }

    /// <summary>亡骨魔死亡 → 两条标记一起移除（规则细节 20 / 24）。</summary>
    [Fact]
    public void DeadDemon_MeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Dead), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>该席位已不是亡骨魔（换角）→ 能力不再存在，标记移除。</summary>
    [Fact]
    public void DemonCharacterChanged_MeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "sweetheart", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>亡骨魔离场（不在座次环上）→ 标记移除。</summary>
    [Fact]
    public void DemonDeparted_MeansNoExpectations()
    {
        var context = Context(
            [(1, "vigormortis", LifeState.Dead), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)],
            seats: [new SeatId(2), new SeatId(3)]);

        var assessment = Source.Evaluate(context);

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>死亡还没落地（待定死亡未裁定 / 被说书人阻止）：击杀事实此时不生效。</summary>
    [Fact]
    public void LivingMinion_MeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Alive), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>
    /// 记录在、击杀却没落地（记录只说明说书人作出了选择）：不算「被亡骨魔**成功**杀死」，
    /// 标记不放置（规则细节 19）。
    /// </summary>
    [Fact]
    public void MissingKillEffect_MeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)],
            killEffect: false));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>被标记的玩家角色不再是爪牙角色 → 两条标记一并移除（规则细节 8 / 20 / 24）。</summary>
    [Fact]
    public void MinionNoLongerMinion_MeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "clockmaker", LifeState.Dead), (3, "dreamer", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>标记移除过就不再回来：已终止的窗口不复原（终止不可逆，R-0012）。</summary>
    [Fact]
    public void TerminatedWindow_IsNotRestored()
    {
        var effect = new PersistentEffect
        {
            Id = new EffectId("standing:vigormortis.retention:1:2"),
            Source = new SeatId(1),
            Ability = new AbilityId("vigormortis.retention"),
            Target = new SeatId(2),
            SourceCharacter = new CharacterId("vigormortis"),
            Window = EffectWindowKind.RetainedAbility,
        };
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)],
            extras:
            [
                new PersistentEffectAppliedEvent { Effect = effect },
                new PersistentEffectTerminatedEvent
                {
                    EffectId = effect.Id,
                    Termination = new EffectTermination
                    {
                        Kind = EffectTerminationKind.NoLongerApplies,
                        Reason = "测试：标记已移除",
                    },
                },
            ]));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>座次 / 角色变化 → 中毒标记随**同一侧**移动（规则细节 23），标识含目标因此换了人。</summary>
    [Fact]
    public void PoisonTargetFollowsTheSameSide()
    {
        var before = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive), (4, "dreamer", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));
        var poisonBefore = Assert.Single(before.Expectations, item => item.Dimension is not null);
        Assert.Equal(new SeatId(3), poisonBefore.Target);

        // 3 号从镇民变成外来者：顺时针最近的镇民落到 4 号——同侧移动，不换到另一侧去。
        var after = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "mutant", LifeState.Alive), (4, "dreamer", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)]));
        var poisonAfter = Assert.Single(after.Expectations, item => item.Dimension is not null);
        Assert.Equal(new SeatId(4), poisonAfter.Target);
        Assert.NotEqual(poisonBefore.Id, poisonAfter.Id);
    }

    /// <summary>亡骨魔醉酒：期望仍在（挂起不是移除，R-0012）；窗口生效与否由效果自身的来源判定回答。</summary>
    [Fact]
    public void DrunkDemon_KeepsExpectations()
    {
        var context = Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)],
            drunkSeats: [1]);

        var assessment = Source.Evaluate(context);

        Assert.True(assessment.IsConclusive);
        Assert.Equal(2, assessment.Expectations.Count);
    }

    /// <summary>亡骨魔死亡但被集骨者重获能力：按「仍握有能力」继续维持两条标记（R-0054）。</summary>
    [Fact]
    public void RegainedDemon_KeepsExpectations()
    {
        var regain = new PersistentEffect
        {
            Id = new EffectId("test:regain:1"),
            Source = new SeatId(9),
            Ability = new AbilityId("bone-collector.regain"),
            Target = new SeatId(1),
            SourceCharacter = new CharacterId("bone-collector"),
            Window = EffectWindowKind.RegainedAbility,
            SourceStateIndependent = true,
        };

        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Dead), (2, "witch", LifeState.Dead), (3, "clockmaker", LifeState.Alive)],
            [new Kill(1, 2, SeatRingDirection.Clockwise)],
            extras: [new PersistentEffectAppliedEvent { Effect = regain }]));

        Assert.True(assessment.IsConclusive);
        Assert.Equal(2, assessment.Expectations.Count);
    }

    /// <summary>没有击杀事实：期望集为空（对账会把遗留效果终止掉）。</summary>
    [Fact]
    public void NoKills_MeansNoExpectations()
    {
        var assessment = Source.Evaluate(Context(
            [(1, "vigormortis", LifeState.Alive), (2, "witch", LifeState.Dead)],
            []));

        Assert.True(assessment.IsConclusive);
        Assert.Empty(assessment.Expectations);
    }

    /// <summary>有席位角色未观测：判定不了（不猜），本次不重算。</summary>
    [Fact]
    public void UnobservedCharacter_IsInconclusive()
    {
        var state = GameStateMachine.Fold(
        [
            Row(1, "vigormortis", LifeState.Alive),
            new SeatStateChangedEvent
            {
                Seat = new SeatId(2),
                Life = LifeState.Dead,
                Drunk = DrunkState.Sober,
                Poison = PoisonState.Healthy,
                Reason = "缺角色",
            },
            new VigormortisKillRecordedEvent
            {
                Demon = new SeatId(1),
                Minion = new SeatId(2),
                Side = SeatRingDirection.Clockwise,
            },
        ]);

        var assessment = Source.Evaluate(new StandingEffectContext
        {
            State = state,
            Seats = [new SeatId(1), new SeatId(2)],
        });

        Assert.False(assessment.IsConclusive);
        Assert.Contains("角色尚未观测", assessment.Note, StringComparison.Ordinal);
    }

    private static StandingEffectContext Context(
        (int Seat, string Character, LifeState Life)[] rows,
        Kill[] kills,
        bool killEffect = true,
        GameEvent[]? extras = null,
        int[]? drunkSeats = null,
        SeatId[]? seats = null)
    {
        var events = new List<GameEvent>();
        foreach (var (seat, character, life) in rows)
        {
            events.Add((drunkSeats ?? []).Contains(seat)
                ? Row(seat, character, life) with { Drunk = DrunkState.Drunk }
                : Row(seat, character, life));
        }

        foreach (var kill in kills)
        {
            if (killEffect)
            {
                events.Add(new InstantaneousEffectAppliedEvent
                {
                    Effect = new InstantaneousEffect
                    {
                        Id = new EffectId($"test:kill:{kill.Demon}:{kill.Minion}"),
                        Source = new SeatId(kill.Demon),
                        Ability = new AbilityId("vigormortis"),
                        Target = new SeatId(kill.Minion),
                    },
                });
            }

            events.Add(new VigormortisKillRecordedEvent
            {
                Demon = new SeatId(kill.Demon),
                Minion = new SeatId(kill.Minion),
                Side = kill.Side,
            });
        }

        if (extras is not null)
        {
            events.AddRange(extras);
        }

        return new StandingEffectContext
        {
            State = GameStateMachine.Fold(events),
            Seats = seats ?? [.. rows.Select(row => new SeatId(row.Seat)).OrderBy(seat => seat.Value)],
        };
    }

    private static SeatStateChangedEvent Row(int seat, string character, LifeState life) => new()
    {
        Seat = new SeatId(seat),
        Character = new CharacterId(character),
        Life = life,
        Drunk = DrunkState.Sober,
        Poison = PoisonState.Healthy,
        Reason = "测试夹具",
    };

    private readonly record struct Kill(int Demon, int Minion, SeatRingDirection? Side);
}
