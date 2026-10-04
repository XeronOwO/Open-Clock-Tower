namespace OpenClockTower.Kernel;

/// <summary>
/// 死亡保护的死因分类：一条保护可能只覆盖其中一种死因（怪咖只防流放致死，R-0048）。
/// </summary>
/// <remarks>
/// 收口点按死因向契约提问；范围由规则层来源自行回答（内核不认识角色 slug，D-0008）。
/// 目前只有白天两条收口：流放计票与 <see cref="DayMachine.CloseDay"/> 的处决收口。
/// </remarks>
public enum DeathProtectionCause
{
    /// <summary>流放致死（reason = <see cref="ExileMachine.ExileDeathReason"/>）。</summary>
    Exile,

    /// <summary>处决致死（reason = <see cref="DayMachine.ExecutionDeathReason"/>）。</summary>
    Execution,
}
