namespace OpenClockTower.Kernel;

/// <summary>
/// 博学者提问依据契约（规则层实现）：回答"这个席位此刻能不能要信息"与"一条裁定怎么结清"。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IArtistQuestionSource"/> 同族：内核声明它需要的外部能力，规则层实现
/// （谁是博学者、提示文案、两条信息的格式、涡流干扰），依赖图保持无环——内核因此不必认识任何角色
/// slug（D-0008：规则数据属于 Rules）。平台口径见 <c>docs/standard/rulings.md</c> R-0057。
/// </para>
/// <para>
/// <see cref="Resolve"/> 返回 null 表示判不了（席位维度没观测齐），由内核显式拒绝（D-0015：不猜）。
/// </para>
/// </remarks>
public interface ISavantQuestionSource
{
    /// <summary>本契约负责的角色。</summary>
    CharacterId Character { get; }

    /// <summary>结清时记账用的能力标识（进能力使用账本）。</summary>
    AbilityId Ability { get; }

    /// <summary>构造裁定点提示（无选项：两条信息由说书人自由填写）。</summary>
    ChoicePrompt BuildPrompt();

    /// <summary>结清一条裁定；返回 null = 判不了（不猜）。</summary>
    SavantQuestionResolution? Resolve(SavantQuestionResolutionContext context);
}
