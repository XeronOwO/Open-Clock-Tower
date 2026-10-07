using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 《梦殒春宵》首版 30 个角色的花名册：角色 slug、类型、中文名与**设置调整**（服务端权威数据）。
/// </summary>
/// <remarks>
/// <para>
/// 来源：<c>docs/standard/terminology.md</c> §9「首版角色清单」——25 个非旅行者角色 2026-10-01 已核对
/// （与百科《梦殒春宵》剧本页、各角色页「角色信息」节的 `英文名` / `角色类型` 交叉比对一致）；
/// 5 名旅行者 2026-10-04 抓取核对（D-0022 首版纳入；流放与死亡口径见 `rulings.md` R-0044–R-0047）。
/// 中文名与 slug 成对（术语表 §1：标识用英文 slug，禁止拼音与转写）。顺序即术语表顺序；
/// 只用于检索与校验，不承载结算语义。旅行者不参与配板（R-0046），<see cref="AsSetupScript"/> 只出四类型池。
/// </para>
/// <para>
/// 两条设置调整见 <c>docs/standard/rulings.md</c> R-0042 依据（百科《设置调整》· 相关角色，
/// 2026-10-04 抓取）：方古「+1 外来者，-1 镇民」、亡骨魔「-1 外来者，+1 镇民」。
/// </para>
/// </remarks>
public static class SectsAndVioletsRoster
{
    private sealed record Profile(
        CharacterId Id,
        CharacterType Type,
        string DisplayName,
        IReadOnlyList<SetupAdjustment> SetupAdjustments);

    private static readonly Profile[] Profiles =
    [
        Plain("clockmaker", CharacterType.Townsfolk, "钟表匠"),
        Plain("dreamer", CharacterType.Townsfolk, "筑梦师"),
        Plain("snake-charmer", CharacterType.Townsfolk, "舞蛇人"),
        Plain("mathematician", CharacterType.Townsfolk, "数学家"),
        Plain("flowergirl", CharacterType.Townsfolk, "卖花女孩"),
        Plain("town-crier", CharacterType.Townsfolk, "城镇公告员"),
        Plain("oracle", CharacterType.Townsfolk, "神谕者"),
        Plain("savant", CharacterType.Townsfolk, "博学者"),
        Plain("seamstress", CharacterType.Townsfolk, "女裁缝"),
        Plain("philosopher", CharacterType.Townsfolk, "哲学家"),
        Plain("artist", CharacterType.Townsfolk, "艺术家"),
        Plain("juggler", CharacterType.Townsfolk, "杂耍艺人"),
        Plain("sage", CharacterType.Townsfolk, "贤者"),
        Plain("mutant", CharacterType.Outsider, "畸形秀演员"),
        Plain("sweetheart", CharacterType.Outsider, "心上人"),
        Plain("barber", CharacterType.Outsider, "理发师"),
        Plain("klutz", CharacterType.Outsider, "呆瓜"),
        Plain("evil-twin", CharacterType.Minion, "镜像双子"),
        Plain("witch", CharacterType.Minion, "女巫"),
        Plain("cerenovus", CharacterType.Minion, "洗脑师"),
        Plain("pit-hag", CharacterType.Minion, "麻脸巫婆"),
        // R-0042：+1 外来者，-1 镇民（百科《方古》· 角色能力 `[+1外来者]`；《设置调整》· 相关角色）。
        new(new CharacterId("fang-gu"), CharacterType.Demon, "方古",
            [new SetupAdjustment(CharacterType.Outsider, 1)]),
        // R-0042：-1 外来者，+1 镇民（百科《亡骨魔》· 角色能力 `[-1外来者]`；《设置调整》· 相关角色）。
        new(new CharacterId("vigormortis"), CharacterType.Demon, "亡骨魔",
            [new SetupAdjustment(CharacterType.Outsider, -1)]),
        Plain("no-dashii", CharacterType.Demon, "诺-达鲺"),
        Plain("vortox", CharacterType.Demon, "涡流"),
        // 旅行者（D-0022 首版纳入）；slug / 中文名见术语表 §9（2026-10-04 抓取核对）。
        Plain("deviant", CharacterType.Traveller, "怪咖"),
        Plain("bone-collector", CharacterType.Traveller, "集骨者"),
        Plain("barista", CharacterType.Traveller, "咖啡师"),
        Plain("harlot", CharacterType.Traveller, "流莺"),
        Plain("butcher", CharacterType.Traveller, "屠夫"),
    ];

    private static Profile Plain(string id, CharacterType type, string displayName) =>
        new(new CharacterId(id), type, displayName, []);

    /// <summary>全部角色（按术语表顺序；含 5 名旅行者）。要「角色列表上的角色」用 <see cref="CharacterList"/>。</summary>
    public static IReadOnlyList<CharacterId> All { get; } =
        Array.AsReadOnly(Profiles.Select(profile => profile.Id).ToArray());

    /// <summary>
    /// **角色列表**上的角色：镇民 / 外来者 / 爪牙 / 恶魔四个类型（按术语表顺序，**不含旅行者**）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 「角色列表」是百科的定义术语：「一叠罗列出对应剧本中可能出现的所有角色及角色能力的纸张」；
    /// 旅行者列在**旅行者列表**上，不在角色列表上（百科《术语汇总》· 2026-10-04 抓取 ·
    /// 「角色列表」/「旅行者列表」/「不在场」）。因此「不在场」= 出现在角色列表上而当前不在游戏中——
    /// **旅行者天然不满足这个定义**。
    /// </para>
    /// <para>
    /// 规则后果（R-0060）：能力既不能把玩家变成旅行者，也不能把旅行者变成非旅行者。
    /// 凡是「指向一个角色 / 让某人变成某角色」的候选表都取这里，别各自按类型过滤——
    /// 麻脸巫婆曾因此把旅行者放进候选（百科《哪些是“可以但不建议”》· 2026-10-04 抓取 ·
    /// 基础规则部分「旅行者的角色转换」）。
    /// </para>
    /// </remarks>
    public static IReadOnlyList<CharacterId> CharacterList { get; } =
        Array.AsReadOnly(Profiles.Where(profile => profile.Type != CharacterType.Traveller)
            .Select(profile => profile.Id)
            .ToArray());

    /// <summary>该角色是否在首版花名册里。</summary>
    public static bool Contains(CharacterId character) =>
        Profiles.Any(profile => profile.Id == character);

    /// <summary>角色类型；不在首版花名册里返回 null——不猜。</summary>
    public static CharacterType? TypeOf(CharacterId character)
    {
        foreach (var profile in Profiles)
        {
            if (profile.Id == character)
            {
                return profile.Type;
            }
        }

        return null;
    }

    /// <summary>中文名；不在首版花名册里返回 null。</summary>
    public static string? DisplayNameOf(CharacterId character)
    {
        foreach (var profile in Profiles)
        {
            if (profile.Id == character)
            {
                return profile.DisplayName;
            }
        }

        return null;
    }

    /// <summary>该角色的设置调整；没有修正或不在册时返回空表——不猜（R-0042）。</summary>
    public static IReadOnlyList<SetupAdjustment> SetupAdjustmentsOf(CharacterId character)
    {
        foreach (var profile in Profiles)
        {
            if (profile.Id == character)
            {
                return profile.SetupAdjustments;
            }
        }

        return [];
    }

    /// <summary>某一类型的全部角色（按术语表顺序）。</summary>
    public static IReadOnlyList<CharacterId> OfType(CharacterType type) =>
        [.. Profiles.Where(profile => profile.Type == type).Select(profile => profile.Id)];

    /// <summary>把花名册转成配板求解的剧本数据（设置修正随角色带上）。</summary>
    public static SetupScript AsSetupScript() => new(
        [.. OfType(CharacterType.Townsfolk).Select(ToPoolEntry)],
        [.. OfType(CharacterType.Outsider).Select(ToPoolEntry)],
        [.. OfType(CharacterType.Minion).Select(ToPoolEntry)],
        [.. OfType(CharacterType.Demon).Select(ToPoolEntry)]);

    private static SetupPoolEntry ToPoolEntry(CharacterId character) =>
        new(character, SetupAdjustmentsOf(character));
}
