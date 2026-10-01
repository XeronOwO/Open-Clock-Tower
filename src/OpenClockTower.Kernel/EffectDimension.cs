namespace OpenClockTower.Kernel;

/// <summary>
/// 持续型效果可以压制的玩家维度：一条效果的「作用面」。
/// </summary>
/// <remarks>
/// 状态账只报事实、不替结算引擎推演维度（D-0015），但「按仍生效的效果重算目标维度」是引擎的义务
/// （D-0015 推论 1）。要重算就必须知道一条效果压制的是哪一维，本枚举就是这条声明。
/// 只覆盖会被效果**持续压制**的两个维度（中毒 / 醉酒）；生死 / 角色 / 阵营的变化走各自的事件路径。
/// </remarks>
public enum EffectDimension
{
    /// <summary>中毒：效果生效 → 中毒；终止或挂起 → 健康。</summary>
    Poison = 0,

    /// <summary>醉酒：效果生效 → 醉酒；终止或挂起 → 清醒。</summary>
    Drunk,
}
