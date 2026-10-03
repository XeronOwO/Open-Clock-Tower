using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 死亡触发家族的共用读数：某条死亡事件发生时，**某个席位当时的角色**。
/// </summary>
/// <remarks>
/// <para>
/// 理发师「必须作为理发师死亡才触发」（R-0033）、贤者「作为贤者被恶魔杀死」（R-0038）、
/// 心上人「作为心上人死亡」（R-0039）共用同一条重建口径：事件流是唯一事实来源，
/// 事件自带的角色维度优先；否则从批前账出发，把排在该事件之前的角色变化逐条折入。
/// </para>
/// <para>
/// 批前账取「本批第一条该席位角色变化的『变化前角色』」（提交管线的统一补全，R-0029）；
/// 没有角色变化时退回 <see cref="EventTriggerContext.State"/> 的当前角色。
/// 角色维度缺失时返回 null——**判不了就不猜**（D-0015），由调用方显式跳过。
/// </para>
/// </remarks>
internal static class DeathTriggerReadings
{
    /// <summary>该席位在 <paramref name="index"/> 这条事件**发生之前**那一刻的角色。</summary>
    /// <param name="context">触发上下文（批后账 + 本批事件）。</param>
    /// <param name="seat">要读的席位。</param>
    /// <param name="index">死亡事件在本批事件里的下标。</param>
    /// <returns>角色；null = 该时刻的角色未观测，判不了。</returns>
    internal static CharacterId? CharacterAt(EventTriggerContext context, SeatId seat, int index)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Events[index] is SeatStateChangedEvent death
            && death.Seat == seat
            && death.Character is { } observed)
        {
            return observed;
        }

        var character = CharacterBeforeBatch(context, seat);
        for (var earlier = 0; earlier < index; earlier++)
        {
            if (context.Events[earlier] is SeatStateChangedEvent { Character: { } value } change
                && change.Seat == seat)
            {
                character = value;
            }
        }

        return character;
    }

    /// <summary>该席位在本批之前的角色；本批没有它的角色变化时就是账上的当前角色。</summary>
    private static CharacterId? CharacterBeforeBatch(EventTriggerContext context, SeatId seat)
    {
        foreach (var gameEvent in context.Events)
        {
            if (gameEvent is SeatStateChangedEvent { Character: not null } change && change.Seat == seat)
            {
                return change.PreviousCharacter;
            }
        }

        return context.State.Seat(seat)?.CharacterValue;
    }
}
