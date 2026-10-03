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

    /// <summary>
    /// 角色触发格（如理发师 / 心上人 / 贤者）：顺序表上为「事件触发的能力在当夜与某人交互」留出的时机。
    /// 进入时只记时间到、不产生请求；是否开交互由触发管线按步骤机事实决定。
    /// 角色不在场 / 已死亡也保留这一格——能力属于死亡触发，不属于格子的持有者本人。
    /// </summary>
    CharacterTrigger,

    /// <summary>黎明：夜晚终点，宣布生死变化。</summary>
    Dawn,
}
