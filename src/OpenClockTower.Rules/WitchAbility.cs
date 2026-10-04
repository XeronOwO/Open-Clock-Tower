using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 女巫能力的共同口径：能力标识、诅咒效果标识，以及「只剩三名存活玩家时失去能力」的判定。
/// </summary>
/// <remarks>
/// <para>
/// 提示（夜晚行动）、触发（提名即死）、存续（失去能力 → 解除诅咒）三处必须用**同一份**判定，
/// 否则会出现"提示说不能行动、触发却仍然杀人"这类分叉。
/// </para>
/// <para>
/// 来源：百科《女巫》· 2026-10-01 抓取 · 角色能力——「每个夜晚，你要选择一名玩家：如果他明天白天
/// 发起提名，他死亡。如果只有三名存活的玩家，你失去此能力」；· 角色简介——「只剩三名玩家存活时，
/// 女巫的诅咒立即解除，女巫也无法再进行夜晚行动」。
/// </para>
/// </remarks>
internal static class WitchAbility
{
    /// <summary>夜间行动的能力标识（进两本账）。</summary>
    internal static readonly AbilityId ActionAbility = new("witch");

    /// <summary>诅咒的能力标识：与夜间行动分开记（同诺-达鲺的击杀 / 常驻中毒）。</summary>
    internal static readonly AbilityId CurseAbility = new("witch.curse");

    /// <summary>女巫的角色标识。</summary>
    internal static readonly CharacterId Witch = new("witch");

    /// <summary>
    /// 诅咒致死的说明（写进 <see cref="SeatStateChangedEvent.Reason"/>）：机器可读的归因在
    /// <see cref="SeatStateChangedEvent.EffectId"/> 与 <see cref="SeatStateChangedEvent.CausedBy"/> 上，
    /// 这一句是给说书人上帝视角直接看的。
    /// </summary>
    internal const string CurseDeathReason = "女巫的诅咒：被诅咒者发起提名即死（提名仍然生效）";

    /// <summary>失去能力的存活人数阈值：存活 ≤ 3 即失去（百科：只剩三名存活玩家）。</summary>
    private const int LostAtAliveCount = 3;

    /// <summary>
    /// 诅咒效果的标识：槽位稳定键（<c>sv:night-2:witch</c>，重进的遍次带 <c>#N</c>）加后缀，重放稳定。
    /// </summary>
    internal static EffectId CurseEffectId(string slotKey) =>
        new($"{slotKey}:curse");

    /// <summary>
    /// 能力此刻是否仍在；null = 席位的生死还没观测齐，判定不了（不猜，D-0015）。
    /// </summary>
    /// <param name="state">当前状态账。</param>
    /// <param name="seats">本局完整座次（存活人数按它算）。</param>
    internal static bool? InForce(GameState state, IReadOnlyList<SeatId> seats)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(seats);

        SeatId? witchSeat = null;
        foreach (var seat in seats)
        {
            if (state.Seat(seat)?.CharacterValue == Witch)
            {
                witchSeat = seat;
                break;
            }
        }

        if (witchSeat is null)
        {
            // 女巫不在场：能力不存在（残留的诅咒按「条件不再满足」解除）。
            return false;
        }

        if (state.Seat(witchSeat.Value)?.LifeValue is not { } witchLife)
        {
            return null;
        }

        if (witchLife == LifeState.Dead)
        {
            // 玩家死亡即失去角色能力（百科《术语汇总》「死亡」）：诅咒由折叠链路终止。
            return false;
        }

        var alive = 0;
        foreach (var seat in seats)
        {
            var life = state.Seat(seat)?.LifeValue;
            if (life is null)
            {
                return null;
            }

            if (life == LifeState.Alive)
            {
                alive++;
            }
        }

        return alive > LostAtAliveCount;
    }
}
