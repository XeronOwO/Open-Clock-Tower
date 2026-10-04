namespace OpenClockTower.Rules;

/// <summary>
/// 角色类型：镇民 / 外来者 / 爪牙 / 恶魔 / 旅行者。
/// </summary>
/// <remarks>
/// 依据：百科《镇民》《外来者》《爪牙》《恶魔》《旅行者》；旅行者首版纳入（D-0022，细则见
/// <c>docs/standard/rulings.md</c> R-0007 / R-0044–R-0047），传奇角色仍不纳入。角色与类型的对照见
/// <c>docs/standard/terminology.md</c> §9「首版角色清单」。
/// </remarks>
public enum CharacterType
{
    /// <summary>镇民：善良阵营。</summary>
    Townsfolk = 0,

    /// <summary>外来者：善良阵营，能力对善良不利。</summary>
    Outsider,

    /// <summary>爪牙：邪恶阵营。</summary>
    Minion,

    /// <summary>恶魔：邪恶阵营，善良需杀光所有恶魔才获胜。</summary>
    Demon,

    /// <summary>旅行者：阵营由说书人私下裁定（可变）；只走流放流程，不占四类型配板名额（R-0044–R-0047）。</summary>
    Traveller,
}
