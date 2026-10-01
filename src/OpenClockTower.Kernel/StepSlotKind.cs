namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤槽位的类型。
/// </summary>
public enum StepSlotKind
{
    /// <summary>有角色行动：进入该槽位会向该玩家定向投递操作请求。</summary>
    Action,

    /// <summary>
    /// 空槽位：角色不在场 / 已死亡 / 被跳过——**照样消耗配额**，
    /// 否则"今晚有几个技能位"会通过时长泄漏（D-0013 §1）。
    /// </summary>
    Empty,

    /// <summary>
    /// 节拍槽位：黄昏 / 爪牙信息 / 恶魔信息 / 信息环节开始等**非角色行动**的顺序表条目。
    /// 消耗配额、不产生请求；与 <see cref="Empty"/>（角色不在场 / 已死亡）语义不同，不互相顶替。
    /// </summary>
    Beat,

    /// <summary>黎明宣布的等待：纳入同一节奏，不是独立计时（D-0013 §3）。</summary>
    DawnWait,
}
