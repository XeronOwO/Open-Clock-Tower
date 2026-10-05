using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 亡骨魔的夜间行动（除首个夜晚）：选择一名玩家，他死亡；他若因此被杀的是**爪牙**，
/// 该爪牙保留自己的角色能力，并由说书人选定一侧、让那一侧最近的镇民中毒。
/// </summary>
/// <remarks>
/// <para>
/// 来源（均为钟楼百科 · 2026-10-01 抓取）：《亡骨魔》· 角色能力——「每个夜晚*，你要选择一名玩家：
/// 他死亡。」「被你杀死的爪牙保留他的能力，且与他邻近的两名镇民之一中毒」；
/// · 规则细节 11–14（唤醒、放置「死亡」标记、爪牙额外放「保留能力」标记、邻近镇民中毒）、
/// 15–24（两类标记的放置 / 移除时机与动态检测）；· 角色简介 1–9。
/// </para>
/// <para>
/// 三条平台口径（<c>docs/standard/rulings.md</c> R-0056）：
/// ① 「选择一名玩家」含自己与已死亡玩家（百科《重要细节》三-1），已死亡者不会再次死亡，
/// 因此既不落击杀事实、也不放置保留能力标记；
/// ② 中毒侧由说书人在**两个候选**（顺时针 / 逆时针最近的镇民）之间选定——平台不替他说书，
/// 只把选择记成事实；全场只有一名镇民时两个方向数到同一人，选择没有实际区别，平台记为顺时针；
/// ③ 麻脸巫婆之夜（创造了恶魔的那一晚）的击杀按统一出口记为待定死亡，「保留能力」载荷随它一起走
/// ——说书人确认时按普通夜晚同一条路径落格，阻止时整条不产生。
/// </para>
/// </remarks>
internal sealed class VigormortisNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => VigormortisAbility.Character;

    /// <inheritdoc />
    public AbilityId Ability => VigormortisAbility.KillAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Seats
            .OrderBy(seat => seat.Value)
            .Select(seat => new DecisionOption
            {
                Value = SeatChoice.Format(seat),
                Preview = $"{seat.Value} 号玩家",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "亡骨魔选择一名玩家：他死亡；若他是爪牙，他保留能力且邻近一侧的镇民中毒"
                + "（可选自己与已死亡玩家，依《重要细节》三-1）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <summary>
    /// 目标是爪牙时的追加裁定点：说书人在「顺时针最近的镇民」与「逆时针最近的镇民」之间选一个。
    /// </summary>
    /// <remarks>
    /// 能力未生效（醉酒 / 中毒 / 死亡）时返回 null：什么都没发生，没有标记可放
    /// （百科《亡骨魔》· 规则细节 16 / 19 的放置条件都要求亡骨魔未醉酒中毒）。
    /// 全场没有镇民 / 两个方向数到同一人时也返回 null——前者没有中毒可言，后者没有可选的余地
    /// （口径与记录形状见 R-0056）。
    /// </remarks>
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Outcome.Effective || !IsMinionTarget(context, out var target))
        {
            return null;
        }

        var (clockwise, counterClockwise) = NeighborTownsfolk(context, target);
        if (clockwise is null && counterClockwise is null)
        {
            return null;
        }

        if (clockwise == counterClockwise)
        {
            // 全场只有一名镇民：两个方向数到同一人，说书人的"选侧"没有实际区别。
            return null;
        }

        return new ChoicePrompt
        {
            Context = $"亡骨魔杀死了爪牙 {target.Value} 号：请选择哪一侧最近的镇民中毒"
                + "（百科《亡骨魔》· 2026-10-01 抓取 · 规则细节 6 / 14；R-0056）",
            Options =
            [
                SideOption(SeatRingDirection.Clockwise, clockwise),
                SideOption(SeatRingDirection.CounterClockwise, counterClockwise),
            ],
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Outcome.Effective)
        {
            // 中毒 / 醉酒 / 死亡：能力不生效——玩家照常选完目标，但什么都不发生（不提示玩家，三-1）。
            return [];
        }

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"亡骨魔的结算缺少合法目标席位：{context.Choice}");

        var life = context.State.Seat(target)?.LifeValue
            ?? throw new InvalidOperationException($"席位 {target.Value} 的生死尚未观测，击杀不替它猜");
        if (life == LifeState.Dead)
        {
            // 已死亡的玩家无法再次死亡（术语《生死》）：能力用过，但不产生死亡事实、也不放保留能力标记。
            return [];
        }

        if (!IsMinionTarget(context, out _))
        {
            return NightKill.Resolve(context, target, Ability, VigormortisAbility.KillReason(context.Actor, target));
        }

        var side = ResolveSide(context, target);
        var retain = VigormortisAbility.RetainEffect(context.Actor, target);

        if (context.PitHagNightActive)
        {
            // 麻脸巫婆之夜：击杀是待定死亡，「保留能力」载荷随它走——确认时才落格（并与死亡同批、
            // 排在死亡之前），阻止时整条不产生（R-0030 第 2 条 / R-0056）。
            return NightKill.Resolve(
                context,
                target,
                Ability,
                VigormortisAbility.KillReason(context.Actor, target),
                new DeferredRetention { RetainEffect = retain, Side = side });
        }

        // 顺序有语义：保留能力窗口排在死亡**之前**，死亡折叠时才判得出"他没有失去能力"——
        // 他名下既有的持续型效果与疯狂要求因此不被终止（百科《死后能力保留》· 2026-10-01 抓取）。
        return
        [
            new PersistentEffectAppliedEvent { Effect = retain },
            new VigormortisKillRecordedEvent
            {
                Demon = context.Actor,
                Minion = target,
                Side = side,
            },
            .. NightKill.Resolve(context, target, Ability, VigormortisAbility.KillReason(context.Actor, target)),
        ];
    }

    /// <summary>
    /// 说书人选定的中毒侧：走过裁定点时取裁定值；没有裁定（全场只有一名镇民 / 没有镇民）时，
    /// 有镇民就记顺时针（两个方向同一人，侧无实际区别）、没有镇民就记 null。
    /// </summary>
    private static SeatRingDirection? ResolveSide(AbilityResolutionContext context, SeatId target)
    {
        if (!string.IsNullOrWhiteSpace(context.Decision))
        {
            return VigormortisAbility.ParseSide(context.Decision)
                ?? throw new InvalidOperationException(
                    $"亡骨魔的中毒侧裁定无法识别：{context.Decision}（只接受 clockwise / counter-clockwise）");
        }

        var (clockwise, counterClockwise) = NeighborTownsfolk(context, target);
        if (clockwise is null && counterClockwise is null)
        {
            return null;
        }

        return SeatRingDirection.Clockwise;
    }

    /// <summary>目标此刻是不是爪牙（角色未观测时不猜，显式失败）。</summary>
    private static bool IsMinionTarget(AbilityResolutionContext context, out SeatId target)
    {
        target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"亡骨魔的结算缺少合法目标席位：{context.Choice}");

        var character = context.State.Seat(target)?.CharacterValue
            ?? throw new InvalidOperationException(
                $"席位 {target.Value} 的角色尚未观测：判不了他是不是爪牙（不猜，D-0015）");

        return SectsAndVioletsRoster.TypeOf(character) == CharacterType.Minion;
    }

    /// <summary>目标两侧最近的镇民席位（跳过非镇民角色）；找不到返回 null。</summary>
    private static (SeatId? Clockwise, SeatId? CounterClockwise) NeighborTownsfolk(
        AbilityResolutionContext context,
        SeatId target)
    {
        var seats = context.Seats;
        var characters = new CharacterId[seats.Count];
        var types = new CharacterType[seats.Count];
        var from = -1;
        for (var index = 0; index < seats.Count; index++)
        {
            var character = context.State.Seat(seats[index])?.CharacterValue
                ?? throw new InvalidOperationException(
                    $"席位 {seats[index].Value} 的角色尚未观测：算不出邻近镇民（不猜）");
            characters[index] = character;
            types[index] = SectsAndVioletsRoster.TypeOf(character)
                ?? throw new InvalidOperationException(
                    $"角色 {character.Value} 不在首版花名册里：算不出邻近镇民");
            if (seats[index] == target)
            {
                from = index;
            }
        }

        if (from < 0)
        {
            throw new InvalidOperationException($"席位 {target.Value} 不在本局座次名单里：算不出邻近镇民");
        }

        return (
            NearestTownsfolk(types, seats, from, step: 1),
            NearestTownsfolk(types, seats, from, step: -1));
    }

    private static SeatId? NearestTownsfolk(CharacterType[] types, IReadOnlyList<SeatId> seats, int from, int step)
    {
        for (var offset = 1; offset < types.Length; offset++)
        {
            var index = ((from + (step * offset)) % types.Length + types.Length) % types.Length;
            if (types[index] == CharacterType.Townsfolk)
            {
                return seats[index];
            }
        }

        return null;
    }

    private static DecisionOption SideOption(SeatRingDirection side, SeatId? seat) => new()
    {
        Value = VigormortisAbility.SideValue(side),
        Preview = seat is { } value
            ? $"{value.Value} 号玩家（{SideText(side)}最近的镇民）"
            : $"{SideText(side)}没有镇民（该侧无人中毒）",
    };

    private static string SideText(SeatRingDirection side) => side switch
    {
        SeatRingDirection.Clockwise => "顺时针",
        SeatRingDirection.CounterClockwise => "逆时针",
        _ => throw new InvalidOperationException($"未知的中毒侧：{side}"),
    };
}
