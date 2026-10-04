namespace OpenClockTower.Kernel;

/// <summary>
/// 角色能力结算契约：把「玩家选完了 / 说书人裁完了」变成落地的事件。
/// </summary>
/// <remarks>
/// <para>
/// 与 <c>OpenClockTower.Rules.INightAction</c>（提示契约）成对：那个只声明「要让谁做什么选择」，
/// 这个负责「选择做完之后发生什么」。两者都由角色实现提供；建表时只把**角色 slug**写进槽位，
/// 结算时再按 slug 从注入的目录里取契约——计划要能随事件 JSON 往返，行为对象不进计划。
/// </para>
/// <para>
/// 硬约束（D-0002）：契约**不许**自己选目标、自己摇随机数、自己决定信息真假；
/// 需要裁量时返回裁定点提示（<see cref="BuildPostChoiceDecision"/>），由说书人拍板。
/// </para>
/// <para>
/// **契约义务**：<see cref="Resolve"/> 产出效果类事件前必须检查
/// <see cref="AbilityResolutionContext.Outcome"/>——中毒 / 醉酒 / 死亡时能力不生效，
/// 不得落任何效果（百科《重要细节》三-3）。信息类能力是例外：未生效时仍由说书人给出
/// （可能为假的）信息，平台只记录与提示、不判定真假。
/// </para>
/// </remarks>
public interface IAbilityResolution
{
    /// <summary>本契约对应的角色。</summary>
    CharacterId Character { get; }

    /// <summary>本契约对应的能力标识（进两本账）。</summary>
    AbilityId Ability { get; }

    /// <summary>
    /// 本契约结算时，除「来源自身状态」之外的失效分类（如涡流对镇民信息能力的必假约束，R-0004 / R-0028）。
    /// </summary>
    /// <remarks>
    /// 只有**会产生信息结果**的能力才实现它：返回的分类与生效判定的分类一起**并列**进失效账本（不硬塞、不抵消）；
    /// 默认空表示本条能力不受这类外部干扰。
    /// </remarks>
    IReadOnlyList<MalfunctionKind> InterferenceMalfunctions(AbilityResolutionContext context) => [];

    /// <summary>
    /// 这次结算是否计入能力使用账本（「用过没有」）。
    /// </summary>
    /// <remarks>
    /// 默认 true：大多数能力一旦被选择就计一次使用——醉酒 / 中毒期间使用即被浪费
    /// （百科《重要细节》三-3），建表期据此不再给第二次机会。
    /// **摇头 / 不用**这类"没有用掉能力"的选择返回 false：哲学家「每局限一次」约束的是「获得」，
    /// 女裁缝摇头时「无事发生」（百科《女裁缝》· 2026-10-01 抓取 · 运作方式 5），两者之后都还能再选。
    /// 口径见 <c>docs/standard/rulings.md</c> R-0036（哲学家）与 R-0040（女裁缝）。
    /// </remarks>
    bool CountsAsUse(AbilityResolutionContext context) => true;

    /// <summary>
    /// 玩家已经作出选择之后，是否还需要说书人再裁定一次（信息类能力需要）。
    /// 返回 null = 直接按 <see cref="Resolve"/> 结算。
    /// </summary>
    ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context);

    /// <summary>
    /// 本契约是否支持在咖啡师「行动两次」窗口内再结算一次（默认 true）。
    /// </summary>
    /// <remarks>
    /// false = 该能力的二次结算语义**未定稿**：当前只有哲学家的「获得能力」（第二次获得是替换
    /// 还是并存未定，登记为 <c>docs/standard/rulings.md</c> R-0053 Open）。步骤机据此不重开本格，
    /// 绝不静默产生第二条授予；能力自己的提示里要说清这件事。
    /// </remarks>
    bool SupportsSecondAction => true;

    /// <summary>
    /// 本契约的能力是不是「每局限一次」：窗口内二次结算的**总使用次数**上限为 2（R-0052 第 3 条）。
    /// </summary>
    /// <remarks>
    /// 普通夜晚能力按「每夜一次」计，不查总使用次数；「每局限一次」的能力（女裁缝等）要查：
    /// 已经用过一次 → 窗口内还能再用一次；一次都没用过 → 下个黄昏前可以合计使用两次。
    /// 判定依据是能力使用账本（<see cref="AbilityUseLedger.UseCount"/>），不是"猜"。
    /// </remarks>
    bool IsLimitedPerGame => false;

    /// <summary>产出本步的事件（效果 / 信息 / 状态变化）；结算结论本身由调用方记录。</summary>
    IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context);
}
