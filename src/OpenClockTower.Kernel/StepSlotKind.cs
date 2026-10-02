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
    /// 触发槽位：标记一个「可能由事件触发的能力在此刻与某人交互」的时机（如理发师格）。
    /// 进入时只记时间到，**不产生请求**——是否开请求由触发管线按步骤机事实决定
    /// （<see cref="BarberNight"/>，口径见 <c>docs/standard/rulings.md</c> R-0033）。
    /// 照样消耗配额（D-0013 §1）；也不做「空槽位有人却没有契约」的阻塞检查——
    /// 这一格的能力属于死亡触发，不属于持有者本人。
    /// </summary>
    Trigger,

    /// <summary>
    /// 节拍槽位：黄昏 / 爪牙信息 / 恶魔信息 / 信息环节开始等**非角色行动**的顺序表条目。
    /// 消耗配额、不产生请求；与 <see cref="Empty"/>（角色不在场 / 已死亡）语义不同，不互相顶替。
    /// </summary>
    Beat,

    /// <summary>黎明宣布的等待：纳入同一节奏，不是独立计时（D-0013 §3）。</summary>
    DawnWait,

    /// <summary>
    /// 白天窗口：白天阶段的唯一槽位，**不消耗配额、不自动推进**；
    /// 只由说书人结束白天（<see cref="CloseDayInput"/>）或强推兜底（<see cref="ForceAdvanceInput"/>）走完。
    /// 白天不走 D-0013 的恒定节奏——那条约束的是夜晚时序防泄漏，白天的提名与投票本身就是公开信息。
    /// </summary>
    DayWindow,
}
