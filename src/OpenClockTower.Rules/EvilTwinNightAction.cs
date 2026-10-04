using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 镜像双子的首夜行动：说书人选择一名对立阵营玩家配对，两名双子互相得知对方的角色。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《镜像双子》· 2026-10-01 抓取 · 运作方式——「初始设置时，在一名善良玩家的角色标记旁
/// 放置『对立双子』提示标记……在首个夜晚，同时唤醒两名双子……指向邪恶双子，并对善良双子展示邪恶双子的
/// 角色标记；指向善良双子，并对邪恶双子展示善良双子的角色标记」；提示标记「移除时机：镜像双子死亡或离场」。
/// </para>
/// <para>
/// 建模：配对 = 一条 <c>Dimension = null</c> 的持续型效果（来源 = 镜像双子、目标 = 对立双子），
/// 于是「镜像双子死亡或离场即移除」由既有折叠链路（SourceDied / SourceLostAbility）自动完成；
/// 阻断与触发读它，口径见 <c>docs/standard/rulings.md</c> R-0025。
/// </para>
/// <para>
/// 首版边界：麻脸巫婆造成的**新镜像双子**走共享的 <see cref="EvilTwinPairing"/> 落「配对 + 互认」
/// （E15 行 12）；「两个双子同阵营需重新配对」暂不在首版——先按 R-0025 增补条目再实现。
/// </para>
/// </remarks>
internal sealed class EvilTwinNightAction : INightAction, IAbilityResolution
{
    /// <inheritdoc />
    public CharacterId Character => EvilTwinAbility.Character;

    /// <inheritdoc />
    public AbilityId Ability => EvilTwinAbility.PairAbility;

    /// <inheritdoc />
    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = EvilTwinPairing.OppositeCandidates(context.State, context.Seats, context.Actor)
            .Select(seat => new DecisionOption
            {
                Value = SeatChoice.Format(seat),
                Preview = $"{seat.Value} 号玩家",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "镜像双子配对：选择一名对立阵营玩家作为「对立双子」"
                + "（百科《镜像双子》· 2026-10-01 抓取 · 提示标记）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }

    /// <inheritdoc />
    public ChoicePrompt? BuildPostChoiceDecision(AbilityResolutionContext context) => null;

    /// <inheritdoc />
    public IReadOnlyList<GameEvent> Resolve(AbilityResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 能力未生效（来源死亡 / 醉酒 / 中毒）：不放置配对标记、也不互认（提示标记的放置条件同族）。
        if (!context.Outcome.Effective)
        {
            return [];
        }

        var target = SeatChoice.Parse(context.Choice)
            ?? throw new InvalidOperationException($"镜像双子的结算缺少合法配对席位：{context.Choice}");

        var actorCharacter = context.State.Seat(context.Actor)?.CharacterValue
            ?? throw new InvalidOperationException("镜像双子自己的角色尚未观测，组不成互认信息");
        var targetCharacter = context.State.Seat(target)?.CharacterValue
            ?? throw new InvalidOperationException($"席位 {target.Value} 的角色尚未观测，组不成互认信息");

        // 配对效果 + 双向互认落在这里，与麻脸巫婆创造新双子的路径同源（EvilTwinPairing）。
        return EvilTwinPairing.Plan(
            context.Actor,
            target,
            actorCharacter,
            targetCharacter,
            EvilTwinAbility.PairEffectId(context.SlotKey));
    }
}
