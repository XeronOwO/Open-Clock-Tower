using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 本夜「会死人的那一段」在哪里结束。
/// </summary>
/// <remarks>
/// <para>
/// 两处消费者共用同一处口径，避免各算各的：
/// ① 麻脸巫婆创造恶魔后的**死亡裁量窗口**关闭点（<c>docs/standard/rulings.md</c> R-0030 第 1 条）；
/// ② 非首个夜晚获得的「首个夜晚」能力的**追加位**——「绝大部分获取信息的能力应该在
/// 所有会造成死亡的效果之后结算」（百科《重要细节》· 2026-10-01 抓取 · 七），口径见 R-0055。
/// </para>
/// <para>
/// 判据是**角色类型**：首版《梦殒春宵》里四个恶魔（方古 / 亡骨魔 / 诺-达鲺 / 涡流）都会造成死亡，
/// 而其余会造成死亡的夜间行动（流莺的造访、麻脸巫婆的死亡裁量）都排在恶魔段**之前**，
/// 因此"最后一个恶魔角色槽位"就是"最后一条可能致死的行动格"（R-0030 第 1 条已登记的同一句话）。
/// 跨剧本扩展时这里要按新剧本的致死行动重算——**这是唯一的改动点**。
/// </para>
/// <para>
/// 只看计划里的角色归属（<see cref="StepSlot.Character"/>），不看那一格此刻有没有行动者：
/// 位置是结构性的，不该随"恶魔此刻死没死"漂移。
/// </para>
/// </remarks>
internal static class NightDeathWindow
{
    /// <summary>本夜最后一条可能致死的行动格下标；计划缺失或没有恶魔格时返回 null。</summary>
    internal static int? LastCapableSlotIndex(StepPlan? plan)
    {
        if (plan is null)
        {
            return null;
        }

        for (var index = plan.Slots.Count - 1; index >= 0; index--)
        {
            if (plan.Slots[index].Character is { } character
                && SectsAndVioletsRoster.TypeOf(character) == CharacterType.Demon)
            {
                return index;
            }
        }

        return null;
    }
}
