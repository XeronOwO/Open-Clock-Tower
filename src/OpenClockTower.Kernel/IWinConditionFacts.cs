namespace OpenClockTower.Kernel;

/// <summary>
/// 规则判定需要的**角色事实端口**：这个角色是不是恶魔 / 旅行者 / 涡流 / 呆瓜，这条能力是不是镜像双子配对。
/// </summary>
/// <remarks>
/// <para>
/// 端口声明在内核、实现放在规则层（<c>OpenClockTower.Rules</c> 的花名册）——与
/// <see cref="IAbilityResolutionCatalog"/> 同一套依赖方向：内核不认识具体剧本数据，
/// 只认识"我求值时需要一个这样的查表"。使用方：胜负求值（<see cref="OutcomeEvaluator"/>）与
/// 流放目标合法性（<see cref="ExileMachine"/>）。
/// </para>
/// <para>
/// 只暴露规则判定真正要读的角色事实（目前五条），不暴露整套角色类型——需求长出来再加，
/// 避免把规则层的数据结构提前搬进内核。
/// </para>
/// </remarks>
public interface IWinConditionFacts
{
    /// <summary>该角色是不是恶魔（常规 · 善良的获胜条件读它）。</summary>
    bool IsDemon(CharacterId character);

    /// <summary>
    /// 该角色是不是旅行者（R-0045 第 4 条：不计入「仅有两名玩家存活」；R-0044 第 2 条：流放目标必须是在局旅行者）。
    /// </summary>
    bool IsTraveller(CharacterId character);

    /// <summary>该角色是不是涡流（R-0026 的黄昏胜负条件读它）。</summary>
    bool IsVortox(CharacterId character);

    /// <summary>该角色是不是呆瓜（R-0027 的死亡选择读它）。</summary>
    bool IsKlutz(CharacterId character);

    /// <summary>这条能力标识是不是「镜像双子配对」（R-0025 的阻断与触发读它）。</summary>
    bool IsEvilTwinPair(AbilityId ability);
}
