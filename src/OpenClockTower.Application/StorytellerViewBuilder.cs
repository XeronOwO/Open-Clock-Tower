using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人视图组装：把会话级派生量（跟踪器）与能力目录折算成投影入参，再交给纯映射
/// <see cref="GameProjection.ForStoryteller"/>。
/// </summary>
/// <remarks>
/// 单独成类是为了让 <see cref="GameSession"/> 的读视图路径保持一行调用：派生量
/// （最近变化 / 卡点起算 / 每步摘要 / 最近作废）都来自 <see cref="SessionTrackers"/>，
/// 不在编排器里展开。预览用的能力标识由角色 → 能力契约目录解析（取不到给 null，不猜）。
/// </remarks>
public static class StorytellerViewBuilder
{
    /// <summary>组装当前说书人视图。</summary>
    /// <param name="machine">步骤机状态；未开局为 null。</param>
    /// <param name="state">状态账。</param>
    /// <param name="health">房间健康位（恢复 / 重建失败后的降级态）。</param>
    /// <param name="sequence">视图对应的最新事件序号。</param>
    /// <param name="trackers">会话级派生跟踪器。</param>
    /// <param name="now">应用层当前时刻（卡点时长用）。</param>
    /// <param name="abilities">角色 → 能力结算契约目录。</param>
    /// <param name="seatNames">公开的「席位 → 玩家名」映射（D-0021）。</param>
    public static StorytellerView Build(
        StepMachineState? machine,
        GameState state,
        RoomHealth health,
        long sequence,
        SessionTrackers trackers,
        DateTimeOffset now,
        IAbilityResolutionCatalog abilities,
        IReadOnlyList<SeatDisplayName> seatNames)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(trackers);
        ArgumentNullException.ThrowIfNull(abilities);

        var slot = machine?.CurrentSlot;
        var slotAbility = slot is { Kind: StepSlotKind.Action, Owner: { } owner }
            ? abilities.Find(owner)?.Ability
            : null;

        return GameProjection.ForStoryteller(
            machine,
            state,
            health,
            sequence,
            trackers.PendingRequestSince,
            now,
            trackers.RecentSeatChanges,
            trackers.AnnotationLedger.Annotations,
            seatNames,
            trackers.LastResolution,
            StepDigestProjection.Build(
                machine,
                state,
                slotAbility,
                slot is null ? null : trackers.ResolutionFor(slot.Id)),
            trackers.LastVoidedRequest);
    }
}
