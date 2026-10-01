using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 《梦殒春宵》首版 25 个角色的花名册：角色 slug、类型与中文名。
/// </summary>
/// <remarks>
/// 来源：<c>docs/standard/terminology.md</c> §9「首版角色清单」· 2026-10-01 已核对——
/// 25 个角色与百科《梦殒春宵》剧本页、各角色页「角色信息」节的 `英文名` / `角色类型`
/// 交叉比对一致；中文名与 slug 成对（术语表 §1：标识用英文 slug，禁止拼音与转写）。
/// 顺序即术语表顺序；只用于检索与校验，不承载结算语义。
/// </remarks>
public static class SectsAndVioletsRoster
{
    private sealed record Profile(CharacterId Id, CharacterType Type, string DisplayName);

    private static readonly Profile[] Profiles =
    [
        new(new CharacterId("clockmaker"), CharacterType.Townsfolk, "钟表匠"),
        new(new CharacterId("dreamer"), CharacterType.Townsfolk, "筑梦师"),
        new(new CharacterId("snake-charmer"), CharacterType.Townsfolk, "舞蛇人"),
        new(new CharacterId("mathematician"), CharacterType.Townsfolk, "数学家"),
        new(new CharacterId("flowergirl"), CharacterType.Townsfolk, "卖花女孩"),
        new(new CharacterId("town-crier"), CharacterType.Townsfolk, "城镇公告员"),
        new(new CharacterId("oracle"), CharacterType.Townsfolk, "神谕者"),
        new(new CharacterId("savant"), CharacterType.Townsfolk, "博学者"),
        new(new CharacterId("seamstress"), CharacterType.Townsfolk, "女裁缝"),
        new(new CharacterId("philosopher"), CharacterType.Townsfolk, "哲学家"),
        new(new CharacterId("artist"), CharacterType.Townsfolk, "艺术家"),
        new(new CharacterId("juggler"), CharacterType.Townsfolk, "杂耍艺人"),
        new(new CharacterId("sage"), CharacterType.Townsfolk, "贤者"),
        new(new CharacterId("mutant"), CharacterType.Outsider, "畸形秀演员"),
        new(new CharacterId("sweetheart"), CharacterType.Outsider, "心上人"),
        new(new CharacterId("barber"), CharacterType.Outsider, "理发师"),
        new(new CharacterId("klutz"), CharacterType.Outsider, "呆瓜"),
        new(new CharacterId("evil-twin"), CharacterType.Minion, "镜像双子"),
        new(new CharacterId("witch"), CharacterType.Minion, "女巫"),
        new(new CharacterId("cerenovus"), CharacterType.Minion, "洗脑师"),
        new(new CharacterId("pit-hag"), CharacterType.Minion, "麻脸巫婆"),
        new(new CharacterId("fang-gu"), CharacterType.Demon, "方古"),
        new(new CharacterId("vigormortis"), CharacterType.Demon, "亡骨魔"),
        new(new CharacterId("no-dashii"), CharacterType.Demon, "诺-达鲺"),
        new(new CharacterId("vortox"), CharacterType.Demon, "涡流"),
    ];

    /// <summary>全部 25 个角色（按术语表顺序）。</summary>
    public static IReadOnlyList<CharacterId> All { get; } =
        Array.AsReadOnly(Profiles.Select(profile => profile.Id).ToArray());

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

    /// <summary>某一类型的全部角色（按术语表顺序）。</summary>
    public static IReadOnlyList<CharacterId> OfType(CharacterType type) =>
        [.. Profiles.Where(profile => profile.Type == type).Select(profile => profile.Id)];
}
