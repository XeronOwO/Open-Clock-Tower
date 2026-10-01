using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 钟表匠的夜间行动契约：不产生玩家选择，由说书人给出本夜的最小距离。
/// </summary>
/// <remarks>
/// <para>
/// 来源：百科《钟表匠》· 2026-10-01 抓取 · 规则细节（L1）：
/// 「当钟表匠即将被唤醒之前，说书人会根据当前场上的情况给出实时的信息」；
/// 「在计算距离时，距离值等同于：恶魔与爪牙这两名玩家之间的玩家数量+1」。
/// </para>
/// <para>
/// 平台不替说书人算（D-0002）：本契约产出**说书人裁定点**（无玩家选项 + StorytellerDecides）。
/// 信息的下发与「可能错误」提示属票据后续项，本契约只到「该谁说、该说什么」。
/// </para>
/// </remarks>
internal sealed class ClockmakerNightAction : INightAction
{
    public CharacterId Character => new("clockmaker");

    public ChoicePrompt BuildPrompt(NightActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ChoicePrompt
        {
            Context = "钟表匠获得信息：说书人给出本夜的最小距离（恶魔与最近爪牙之间的人数 + 1）",
            Options = [],
            OnNoOption = NoOptionBehavior.StorytellerDecides,
        };
    }
}
