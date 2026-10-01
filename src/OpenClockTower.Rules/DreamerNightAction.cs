using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 筑梦师的夜间行动契约：每夜选择一名其他玩家。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《筑梦师》· 2026-10-01 抓取 · 角色简介 / 运作方式（L4；该页无「规则细节」小节）：
/// 「每个夜晚，筑梦师需要选择一名其他玩家」「不能选择自己和旅行者作为目标」。
/// </para>
/// <para>
/// 首版不含旅行者（R-0007 未决），目标集合 = 本局席位 − 自己；来源未按生死限制目标，
/// 因此不过滤生死（首夜本来也无人死亡）。说书人给哪两枚标记、给不给「可能错误」提示
/// 属结算引擎（票据后续项）。
/// </para>
/// </remarks>
internal sealed class DreamerNightAction : INightAction
{
    public CharacterId Character => new("dreamer");

    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Seats
            .Where(seat => seat != context.Actor)
            .OrderBy(seat => seat.Value)
            .Select(seat => new DecisionOption
            {
                Value = $"seat:{seat.Value}",
                Preview = $"{seat.Value} 号玩家",
            })
            .ToArray();

        return new ChoicePrompt
        {
            Context = "筑梦师选择一名其他玩家（不能选自己；首版没有旅行者）",
            Options = options,
            OnNoOption = NoOptionBehavior.BlockAndAlert,
        };
    }
}
