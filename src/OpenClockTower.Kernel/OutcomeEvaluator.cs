namespace OpenClockTower.Kernel;

/// <summary>
/// 胜负求值：给定（状态账 + 本局席位 + 白天账 + 本批事件 + 角色事实），算出本批之后的胜负结论。
/// </summary>
/// <remarks>
/// <para>
/// 纯计算（D-0008）：没有时间、随机、IO；输入相同必得同一结论。求值**不改账、不产事件**——
/// 产事件由应用层编排（同 <c>SessionSettlement</c> 的既有分工）。
/// </para>
/// <para>
/// 判定时机与优先级是项目口径（<c>docs/standard/rulings.md</c> R-0024）：
/// </para>
/// <list type="number">
/// <item><description>特殊条件优先于常规条件；同一层内双方同时满足 → <b>善良获胜</b>；</description></item>
/// <item><description>常规条件：所有恶魔死亡 → 善良；场上仅剩 ≤2 名玩家存活 → 邪恶（平台口径：一步跨过 2 也成立，
/// 因为 R-0008 要求事务提交后统一判定，不允许中途插入判定）；</description></item>
/// <item><description>镜像双子配对生效且两名双子都存活时，善良的获胜条件（常规与特殊）被阻断（R-0025 第 2 条）。</description></item>
/// </list>
/// <para>
/// **不猜**（D-0015）：任何一条条件只要它需要的观测不齐（席位缺角色 / 生死 / 阵营），该条一律返回"不成立"，
/// 绝不用默认值补齐。
/// </para>
/// </remarks>
public static class OutcomeEvaluator
{
    /// <summary>求值；没有条件成立返回 null（游戏继续）。</summary>
    /// <exception cref="ArgumentNullException">入参为 null。</exception>
    public static GameOutcome? Evaluate(OutcomeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        GameOutcome? goodSpecial = null;
        GameOutcome? evilSpecial = null;

        foreach (var gameEvent in context.Events)
        {
            switch (gameEvent)
            {
                case ExecutedEvent executed:
                    evilSpecial ??= EvilTwinExecuted(context, executed.Seat);
                    break;
                case DayClosedEvent closed:
                    evilSpecial ??= VortoxNoExecution(context, closed.DayNumber);
                    break;
                case KlutzChoiceMadeEvent choice when KlutzFactionLoses(context, choice) is { } outcome:
                    if (outcome.Winner == Alignment.Good)
                    {
                        goodSpecial ??= outcome;
                    }
                    else
                    {
                        evilSpecial ??= outcome;
                    }

                    break;
            }
        }

        // 镜像双子阻断：善良的获胜条件（常规与特殊）都不成立。
        var goodWinBlocked = IsGoodWinBlocked(context);
        if (goodWinBlocked)
        {
            goodSpecial = null;
        }

        var goodRegular = DemonsAllDead(context, goodWinBlocked);
        var evilRegular = TwoPlayersAlive(context);

        if (goodSpecial is not null && evilSpecial is not null)
        {
            return goodSpecial;
        }

        return goodSpecial ?? evilSpecial ?? goodRegular ?? evilRegular;
    }

    /// <summary>
    /// 常规 · 善良：所有恶魔均死亡（含「运行期恶魔角色清零」）；观测不齐或善良获胜被阻断时返回 null。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 口径见 <c>docs/standard/rulings.md</c> R-0029：
    /// </para>
    /// <list type="bullet">
    /// <item><description>场上还有存活的恶魔角色 → 条件不成立；</description></item>
    /// <item><description>恶魔角色都在、且都已死亡 → 善良获胜（R-0024）；</description></item>
    /// <item><description>**当前没有任何恶魔角色**：本批事件里存在「恶魔 → 非恶魔」的角色变化（麻脸巫婆等）
    /// → 判善良获胜（运行期清零）；否则**不判**——「没有任何恶魔」的配置错误 / 非剧本夹具
    /// 不该被静默判成一局结束（R-0024 第 4 条的原始目的，R-0029 第 2 条）。</description></item>
    /// </list>
    /// </remarks>
    private static GameOutcome? DemonsAllDead(OutcomeContext context, bool goodWinBlocked)
    {
        if (context.Seats.Count == 0)
        {
            return null;
        }

        var demons = 0;
        foreach (var seat in context.Seats)
        {
            var entry = context.State.Seat(seat);
            if (entry?.LifeValue is not { } life || entry.CharacterValue is not { } character)
            {
                return null;
            }

            if (!context.Characters.IsDemon(character))
            {
                continue;
            }

            demons++;
            if (life == LifeState.Alive)
            {
                return null;
            }
        }

        if (goodWinBlocked)
        {
            return null;
        }

        if (demons == 0 && !DemonClearedInThisBatch(context))
        {
            return null;
        }

        return new GameOutcome
        {
            Winner = Alignment.Good,
            Condition = OutcomeCondition.DemonsAllDead,
            Detail = demons == 0
                ? "恶魔角色已不在场（角色变更清空）：善良阵营获胜"
                    + "（百科《规则概要》四 · 2026-10-01 抓取；平台口径见 rulings.md R-0029）。"
                : "所有恶魔均已死亡：善良阵营获胜"
                    + "（百科《规则概要》四 · 2026-10-01 抓取）。",
        };
    }

    /// <summary>
    /// 本批事件里是否存在「恶魔 → 非恶魔」的角色变化（R-0029 第 3–4 条的判据）。
    /// </summary>
    /// <remarks>
    /// 只看**本批**：求值器不持有局级历史（D-0015），而角色清空是当批发生的事实；
    /// 变化前的角色由提交管线写进 <see cref="SeatStateChangedEvent.PreviousCharacter"/>。
    /// </remarks>
    private static bool DemonClearedInThisBatch(OutcomeContext context)
    {
        foreach (var gameEvent in context.Events)
        {
            if (gameEvent is not SeatStateChangedEvent { Character: { } current, PreviousCharacter: { } previous })
            {
                continue;
            }

            if (context.Characters.IsDemon(previous) && !context.Characters.IsDemon(current))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>常规 · 邪恶：场上仅剩两名玩家存活（旅行者不计入，首版无旅行者）。</summary>
    private static GameOutcome? TwoPlayersAlive(OutcomeContext context)
    {
        if (context.Seats.Count == 0)
        {
            return null;
        }

        var alive = 0;
        foreach (var seat in context.Seats)
        {
            var entry = context.State.Seat(seat);
            if (entry?.LifeValue is not { } life)
            {
                return null;
            }

            if (life == LifeState.Alive)
            {
                alive++;
            }
        }

        return alive > 2
            ? null
            : new GameOutcome
            {
                Winner = Alignment.Evil,
                Condition = OutcomeCondition.TwoPlayersAlive,
                Detail = $"场上仅剩 {alive} 名玩家存活：邪恶阵营获胜"
                    + "（百科《规则概要》四 · 2026-10-01 抓取）。",
            };
    }

    /// <summary>特殊 · 邪恶：双子里善良阵营的一方被处决（看处决事实，不看是否死亡；R-0025 第 3 条）。</summary>
    private static GameOutcome? EvilTwinExecuted(OutcomeContext context, SeatId executed)
    {
        foreach (var effect in context.State.PersistentEffects)
        {
            if (effect.IsTerminated || !context.Characters.IsEvilTwinPair(effect.Ability))
            {
                continue;
            }

            if (effect.Source != executed && effect.Target != executed)
            {
                continue;
            }

            // 能力不生效（来源死亡 / 醉酒 / 中毒）或无法判定 → 不触发（R-0025 第 3–4 条）。
            if (context.State.IsOperative(effect) != true)
            {
                continue;
            }

            if (context.State.Seat(executed)?.Alignment?.Value != Alignment.Good)
            {
                continue;
            }

            return new GameOutcome
            {
                Winner = Alignment.Evil,
                Condition = OutcomeCondition.EvilTwinGoodTwinExecuted,
                Detail = $"镜像双子的善良方（{executed.Value} 号）被处决：邪恶阵营获胜"
                    + "（百科《镜像双子》· 2026-10-01 抓取）。",
            };
        }

        return null;
    }

    /// <summary>特殊 · 邪恶：涡流存活且黄昏时今天无人被处决（R-0026）。</summary>
    private static GameOutcome? VortoxNoExecution(OutcomeContext context, int dayNumber)
    {
        var day = context.Day?.Days.FirstOrDefault(record => record.DayNumber == dayNumber);
        if (day is null || day.Executed is not null)
        {
            return null;
        }

        var vortox = context.State.Seats.FirstOrDefault(entry =>
            entry.CharacterValue is { } character
            && context.Characters.IsVortox(character)
            && entry.LifeValue == LifeState.Alive
            && entry.DrunkValue == DrunkState.Sober
            && entry.PoisonValue == PoisonState.Healthy);
        if (vortox is null)
        {
            return null;
        }

        return new GameOutcome
        {
            Winner = Alignment.Evil,
            Condition = OutcomeCondition.VortoxNoExecution,
            Detail = $"第 {dayNumber} 天黄昏：今天无人被处决，而涡流（{vortox.Seat.Value} 号）存活："
                + "邪恶阵营获胜（百科《涡流》· 2026-10-01 抓取）。",
        };
    }

    /// <summary>特殊 · 呆瓜选择：选中邪恶 → 呆瓜当时所在阵营落败，对侧获胜（R-0027 第 3 条）。</summary>
    private static GameOutcome? KlutzFactionLoses(OutcomeContext context, KlutzChoiceMadeEvent choice)
    {
        var target = context.State.Seat(choice.Target)?.Alignment?.Value;
        var klutz = context.State.Seat(choice.Klutz)?.Alignment?.Value;
        if (target is null || klutz is null)
        {
            return null;
        }

        if (target != Alignment.Evil)
        {
            return null;
        }

        var winner = klutz == Alignment.Good ? Alignment.Evil : Alignment.Good;
        return new GameOutcome
        {
            Winner = winner,
            Condition = OutcomeCondition.KlutzChoiceFactionLoses,
            Detail = $"呆瓜（{choice.Klutz.Value} 号，{Describe(klutz.Value)}阵营）选中了邪恶玩家"
                + $"（{choice.Target.Value} 号）：其所在阵营落败，{Describe(winner)}阵营获胜"
                + "（百科《呆瓜》· 2026-10-01 抓取）。",
        };
    }

    /// <summary>
    /// 镜像双子阻断：配对未终止、来源能力生效、且两名双子都存活时，善良阵营无法获胜。
    /// 观测不齐一律按"未确认生效"处理，不阻断（不猜，D-0015）。
    /// </summary>
    private static bool IsGoodWinBlocked(OutcomeContext context)
    {
        foreach (var effect in context.State.PersistentEffects)
        {
            if (effect.IsTerminated || !context.Characters.IsEvilTwinPair(effect.Ability))
            {
                continue;
            }

            if (context.State.IsOperative(effect) != true)
            {
                continue;
            }

            var sourceAlive = context.State.Seat(effect.Source)?.LifeValue == LifeState.Alive;
            var targetAlive = context.State.Seat(effect.Target)?.LifeValue == LifeState.Alive;
            if (sourceAlive && targetAlive)
            {
                return true;
            }
        }

        return false;
    }

    private static string Describe(Alignment alignment) =>
        alignment == Alignment.Good ? "善良" : "邪恶";
}
