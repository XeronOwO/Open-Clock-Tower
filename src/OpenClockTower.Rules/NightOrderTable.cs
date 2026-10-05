using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 《梦殒春宵》完整夜晚顺序表：两个阶段 × 两种口径。
/// </summary>
/// <remarks>
/// <para>
/// 来源（references/wiki-index.json，2026-10-01 抓取）：
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="NightOrderVariant.Original"/>：百科《梦殒春宵》· 2026-10-01 抓取 · 夜晚顺序表。
/// </description></item>
/// <item><description>
/// <see cref="NightOrderVariant.Recommended"/>：百科《夜晚行动顺序一览》· 2026-10-01 抓取 ·
/// 首个夜晚 / 其他夜晚。
/// </description></item>
/// </list>
/// <para>
/// 两口径的差异、平台默认与「说书人可自选」的归属：docs/standard/rulings.md R-0014。
/// 表上的顺序即唤醒顺序；触发条件与选项不在这里（结算引擎与角色实现的职责，架构 §2.6）。
/// </para>
/// </remarks>
public static class NightOrderTable
{
    /// <summary>取某个阶段、某个口径的完整夜晚顺序。</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// 阶段不是首夜 / 其他夜晚：白天与结算中没有夜晚顺序表。
    /// </exception>
    public static IReadOnlyList<NightOrderEntry> For(GamePhase phase, NightOrderVariant variant) =>
        (phase, variant) switch
        {
            (GamePhase.FirstNight, NightOrderVariant.Original) => FirstNightOriginal,
            (GamePhase.OtherNight, NightOrderVariant.Original) => OtherNightOriginal,
            (GamePhase.FirstNight, NightOrderVariant.Recommended) => FirstNightRecommended,
            (GamePhase.OtherNight, NightOrderVariant.Recommended) => OtherNightRecommended,
            _ => throw new ArgumentOutOfRangeException(
                nameof(phase),
                phase,
                "夜晚顺序表只覆盖首夜与其他夜晚"),
        };

    /// <summary>
    /// 该角色在某个阶段、某个口径的顺序表上有没有**角色行动**格（触发格不算行动格）。
    /// </summary>
    /// <remarks>
    /// 消费者的两处判据共用它，避免"建表 / 结算各判一次"漂移：哲学家的落格（R-0036）与
    /// 「非首个夜晚获得的『首个夜晚』能力」的追加判定（R-0055）。
    /// </remarks>
    public static bool HasAction(CharacterId character, GamePhase phase, NightOrderVariant variant) =>
        For(phase, variant).Any(entry =>
            entry.Character == character && entry.Kind == NightOrderEntryKind.CharacterAction);

    private static readonly IReadOnlyList<NightOrderEntry> FirstNightOriginal =
        Array.AsReadOnly<NightOrderEntry>(
            [
                Step(NightOrderEntryKind.Dusk),

                // 旅行者黄昏行动（D5 / R-0052）：咖啡师首个夜晚也行动，插在 Dusk 步之后。
                // 《夜晚行动顺序一览》· 2026-10-04 抓取 · 首个夜晚黄昏行括号：官员、窃贼、学徒、咖啡师。
                Action("barista"),
                Step(NightOrderEntryKind.MinionInfo),
                Step(NightOrderEntryKind.DemonInfo),
                Action("philosopher"),
                Action("snake-charmer"),
                Action("evil-twin"),
                Action("witch"),
                Action("cerenovus"),
                Action("clockmaker"),
                Action("dreamer"),
                Action("seamstress"),
                Action("mathematician"),
                Step(NightOrderEntryKind.Dawn),
            ]);

    private static readonly IReadOnlyList<NightOrderEntry> OtherNightOriginal =
        Array.AsReadOnly<NightOrderEntry>(
            [
                Step(NightOrderEntryKind.Dusk),

                // 旅行者黄昏行动（D5 / R-0051 / R-0052 / R-0054）：其他夜晚的顺序是
                // 咖啡师 → 流莺 → 集骨者（《夜晚行动顺序一览》· 2026-10-04 抓取 · 其他夜晚黄昏行括号：
                // 官员、窃贼、学徒、咖啡师、流莺、集骨者、公爵夫人）。
                Action("barista"),
                Action("harlot"),
                Action("bone-collector"),
                Action("philosopher"),
                Action("snake-charmer"),
                Action("witch"),
                Action("cerenovus"),
                Action("pit-hag"),
                Action("fang-gu"),
                Action("vigormortis"),
                Action("no-dashii"),
                Action("vortox"),
                Trigger("barber"),
                Trigger("sweetheart"),
                Trigger("sage"),
                Action("dreamer"),
                Action("flowergirl"),
                Action("town-crier"),
                Action("oracle"),
                Action("seamstress"),
                Action("juggler"),
                Action("mathematician"),
                Step(NightOrderEntryKind.Dawn),
            ]);

    private static readonly IReadOnlyList<NightOrderEntry> FirstNightRecommended =
        Array.AsReadOnly<NightOrderEntry>(
            [
                Step(NightOrderEntryKind.Dusk),

                // 旅行者黄昏行动（D5 / R-0052）：与 Original 口径同改（推荐口径的首个夜晚黄昏行同样含咖啡师）。
                Action("barista"),
                Action("philosopher"),
                Step(NightOrderEntryKind.MinionInfo),
                Step(NightOrderEntryKind.DemonInfo),
                Action("snake-charmer"),
                Action("evil-twin"),
                Action("witch"),
                Action("cerenovus"),
                Action("clockmaker"),
                Action("dreamer"),
                Action("seamstress"),
                Action("mathematician"),
                Step(NightOrderEntryKind.Dawn),
            ]);

    private static readonly IReadOnlyList<NightOrderEntry> OtherNightRecommended =
        Array.AsReadOnly<NightOrderEntry>(
            [
                Step(NightOrderEntryKind.Dusk),

                // 旅行者黄昏行动（D5 / R-0051 / R-0052 / R-0054）：与 Original 口径同改（两口径同改见票据 D5）。
                Action("barista"),
                Action("harlot"),
                Action("bone-collector"),
                Action("philosopher"),
                Action("pit-hag"),
                Action("snake-charmer"),
                Action("witch"),
                Action("cerenovus"),
                Action("fang-gu"),
                Action("no-dashii"),
                Action("vortox"),
                Action("vigormortis"),
                Trigger("barber"),
                Trigger("sweetheart"),
                Trigger("sage"),
                Step(NightOrderEntryKind.InformationActionsBegin),
                Action("dreamer"),
                Action("flowergirl"),
                Action("town-crier"),
                Action("oracle"),
                Action("seamstress"),
                Action("juggler"),
                Action("mathematician"),
                Step(NightOrderEntryKind.Dawn),
            ]);

    private static NightOrderEntry Step(NightOrderEntryKind kind) => NightOrderEntry.Step(kind);

    private static NightOrderEntry Action(string character) => NightOrderEntry.Action(new CharacterId(character));

    private static NightOrderEntry Trigger(string character) => NightOrderEntry.Trigger(new CharacterId(character));
}
