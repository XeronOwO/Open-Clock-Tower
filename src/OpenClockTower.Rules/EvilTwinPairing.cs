using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 镜像双子「配对 + 双向互认」的共享落地：一条 <c>Dimension = null</c> 的持续型效果，
/// 外加两条只发给当事人本人的信息（D-0012 §4.3）。
/// </summary>
/// <remarks>
/// <para>
/// 两个入口必须**同源**：首夜的 <see cref="EvilTwinNightAction"/> 与麻脸巫婆创造新镜像双子时的
/// 后续裁定——事件结构、候选口径与文本形状完全一致，阻断 / 触发才有唯一的口径
/// （<c>docs/standard/rulings.md</c> R-0025 第 1–3 条）。
/// </para>
/// <para>
/// 来源：百科《镜像双子》· 2026-10-01 抓取 · 运作方式——「在首个夜晚，同时唤醒两名双子……
/// 指向邪恶双子，并对善良双子展示邪恶双子的角色标记」；提示标记——「放置时机：……或有新的
/// 镜像双子被创造出来时」「选择与当前的镜像双子对立阵营的任意一名玩家」「移除时机：镜像双子死亡或离场」。
/// </para>
/// </remarks>
internal static class EvilTwinPairing
{
    /// <summary>构造「配对效果 + 双向互认」的三条事件（顺序稳定：效果在前，信息在后）。</summary>
    /// <param name="twin">镜像双子席位（配对效果的来源）。</param>
    /// <param name="opposite">对立双子席位（配对效果的目标）。</param>
    /// <param name="twinCharacter">配对时刻镜像双子的角色（来源换角色即终止）。</param>
    /// <param name="oppositeCharacter">配对时刻对立双子的角色（互认信息里告诉对方的角色）。</param>
    /// <param name="effectId">配对效果的稳定标识（两个入口各自用所在槽位构造，重放与终止按它认人）。</param>
    internal static IReadOnlyList<GameEvent> Plan(
        SeatId twin,
        SeatId opposite,
        CharacterId twinCharacter,
        CharacterId oppositeCharacter,
        EffectId effectId) =>
    [
        new PersistentEffectAppliedEvent
        {
            Effect = new PersistentEffect
            {
                Id = effectId,
                Source = twin,
                Ability = EvilTwinAbility.PairAbility,
                Target = opposite,
                SourceCharacter = twinCharacter,
                Dimension = null,
            },
        },

        // 双向互认：两名双子各自得知对方的角色（信息只发给本人，D-0012 §4.3）。
        new InformationResultIssuedEvent
        {
            Recipient = twin,
            Ability = EvilTwinAbility.PairAbility,
            Content = $"你的对立双子是 {opposite.Value} 号玩家，其角色为「{Display(oppositeCharacter)}」",
            MayBeFalse = false,
            Note = "双子互认（百科《镜像双子》· 2026-10-01 抓取 · 运作方式）",
        },
        new InformationResultIssuedEvent
        {
            Recipient = opposite,
            Ability = EvilTwinAbility.PairAbility,
            Content = $"{twin.Value} 号玩家是你的对立双子，其角色为「{Display(twinCharacter)}」",
            MayBeFalse = false,
            Note = "双子互认（百科《镜像双子》· 2026-10-01 抓取 · 运作方式）",
        },
    ];

    /// <summary>
    /// 对立双子的合法候选：除镜像双子外、阵营与之相对的所有玩家（不看生死——已死亡的玩家
    /// 同样在列，依《镜像双子》提示与技巧「你可以选择恶魔或一名死亡的玩家作为对立双子」）。
    /// </summary>
    /// <exception cref="InvalidOperationException">镜像双子或某候选席位的阵营尚未观测（不猜，D-0015）。</exception>
    internal static IReadOnlyList<SeatId> OppositeCandidates(
        GameState state,
        IReadOnlyList<SeatId> seats,
        SeatId twin)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);

        var alignment = state.Seat(twin)?.Alignment?.Value
            ?? throw new InvalidOperationException(
                $"席位 {twin.Value} 的阵营尚未观测：列不出对立双子的合法候选（开局分配本应补全阵营，R-0023）");

        return
        [
            .. seats
                .Where(seat => seat != twin)
                .Where(seat => state.Seat(seat)?.Alignment?.Value == Opposite(alignment))
                .OrderBy(seat => seat.Value),
        ];
    }

    private static Alignment Opposite(Alignment alignment) =>
        alignment == Alignment.Good ? Alignment.Evil : Alignment.Good;

    private static string Display(CharacterId character) =>
        SectsAndVioletsRoster.DisplayNameOf(character) ?? character.Value;
}
