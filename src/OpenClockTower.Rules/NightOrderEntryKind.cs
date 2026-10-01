namespace OpenClockTower.Rules;

/// <summary>
/// 夜晚顺序表上一条目的种类。
/// </summary>
/// <remarks>
/// 依据 D-0013 §1：步骤表按剧本的**完整夜晚顺序表**展开——表上不仅有角色行动，
/// 也有黄昏、黎明与首夜的信息环节；没有夜晚行动的角色不出现在表上。
/// 两套口径与来源见 <see cref="NightOrderTable"/> 与 docs/standard/rulings.md R-0014。
/// </remarks>
public enum NightOrderEntryKind
{
    /// <summary>黄昏：夜晚起点，检查所有玩家闭眼。</summary>
    Dusk,

    /// <summary>爪牙信息（首夜）：七名或更多玩家时，向爪牙展示「他是恶魔」等信息。</summary>
    MinionInfo,

    /// <summary>恶魔信息（首夜）：七名或更多玩家时，向恶魔展示爪牙与不在场的善良角色。</summary>
    DemonInfo,

    /// <summary>
    /// 信息类角色行动开始（仅推荐口径）：中途获得的首夜信息能力从此处起触发；
    /// 多项同时触发时由说书人决定顺序。来源：百科《夜晚行动顺序一览》· 其他夜晚。
    /// </summary>
    InformationActionsBegin,

    /// <summary>某个角色的夜晚行动；角色不在场 / 已死亡时对应空槽位（D-0013 §1）。</summary>
    CharacterAction,

    /// <summary>黎明：夜晚终点，宣布生死变化。</summary>
    Dawn,
}
