using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 建表：按剧本的完整夜晚顺序表与当前状态账，展开今晚的步骤序列。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0013 §1：步骤表按**剧本的完整夜晚顺序表**展开，不按在场角色展开——
/// 角色不在场 / 已死亡 → 生成空槽位（<see cref="StepSlotKind.Empty"/>），照样走完配额。
/// 非角色条目（黄昏 / 爪牙信息 / 恶魔信息 / 信息环节开始 / 黎明）→ 节拍或黎明等待槽位。
/// </para>
/// <para>
/// **不猜**：席位没有角色、在场角色的生死未观测、行动契约未实现、角色出现在多个席位，
/// 一律返回失败结果，由调用方拒绝开夜；禁止把"不知道"映射成默认值（D-0015 的同一原则）。
/// </para>
/// <para>
/// 角色合法性（是否在首版花名册里）由分配闸独占；建表器只处理夜晚顺序表上的角色——
/// 花名册之外或不在表上的角色没有槽位，不属于建表器的拒绝面。
/// </para>
/// <para>
/// 纯函数（D-0008）：相同请求必产出相同计划。失败码形如 <c>plan.seat_unassigned</c>，
/// 调用方补上自己的前缀后对外呈现。
/// </para>
/// </remarks>
public static class NightPlanBuilder
{
    /// <summary>按请求建表。</summary>
    /// <exception cref="ArgumentNullException">请求为 null。</exception>
    /// <exception cref="InvalidOperationException">夜晚顺序表数据缺陷（如槽位标识重复）。</exception>
    public static NightPlanOutcome Build(NightPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Enum.IsDefined(request.Variant))
        {
            return NightPlanOutcome.Failure("plan.variant_invalid", $"未知的夜晚顺序口径：{request.Variant}");
        }

        var phase = request.NightNumber == 1 ? GamePhase.FirstNight : GamePhase.OtherNight;
        var entries = NightOrderTable.For(phase, request.Variant);

        // 开局完整性：每个席位都必须有角色，"不在场"与"忘了分配"才区分得开。
        var unassigned = request.Seats
            .Where(seat => request.State.Seat(seat)?.CharacterValue is null)
            .Select(seat => seat.Value)
            .ToArray();
        if (unassigned.Length > 0)
        {
            return NightPlanOutcome.Failure(
                "plan.seat_unassigned",
                $"以下席位还没有角色：{string.Join(", ", unassigned)}");
        }

        var slots = new List<StepSlot>(entries.Count);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var tag = TagOf(entry);
            if (!tags.Add(tag))
            {
                throw new InvalidOperationException($"夜晚顺序表数据缺陷：槽位标识 {tag} 重复");
            }

            if (entry.Kind != NightOrderEntryKind.CharacterAction)
            {
                slots.Add(BuildStepSlot(entry.Kind, tag));
                continue;
            }

            if (entry.Character is not { } character)
            {
                throw new InvalidOperationException("夜晚顺序表数据缺陷：角色条目没有角色");
            }

            var (slot, failure) = BuildCharacterSlot(request, character, tag);
            if (failure is not null)
            {
                return failure;
            }

            slots.Add(slot!);
        }

        return NightPlanOutcome.Success(new StepPlan
        {
            Label = $"sv:night-{request.NightNumber}",
            Phase = phase,
            Variant = request.Variant.ToString(),
            Slots = slots,
        });
    }

    /// <summary>顺序表条目的槽位标识：同一计划内唯一，操作请求标识由它派生。</summary>
    private static string TagOf(NightOrderEntry entry) => entry.Kind switch
    {
        NightOrderEntryKind.Dusk => "dusk",
        NightOrderEntryKind.MinionInfo => "minion-info",
        NightOrderEntryKind.DemonInfo => "demon-info",
        NightOrderEntryKind.InformationActionsBegin => "information-actions-begin",
        NightOrderEntryKind.CharacterAction => entry.Character is { } character
            ? character.Value
            : throw new InvalidOperationException("夜晚顺序表数据缺陷：角色条目没有角色"),
        NightOrderEntryKind.Dawn => "dawn",
        _ => throw new InvalidOperationException($"夜晚顺序表数据缺陷：未知条目种类 {entry.Kind}"),
    };

    /// <summary>非角色条目 → 节拍 / 黎明槽位（都不产生请求，照样消耗配额）。</summary>
    private static StepSlot BuildStepSlot(NightOrderEntryKind kind, string tag) => kind switch
    {
        NightOrderEntryKind.Dawn => StepSlot.DawnWait(new StepSlotId(tag)),
        NightOrderEntryKind.Dusk
            or NightOrderEntryKind.MinionInfo
            or NightOrderEntryKind.DemonInfo
            or NightOrderEntryKind.InformationActionsBegin => StepSlot.Beat(new StepSlotId(tag)),
        _ => throw new InvalidOperationException($"夜晚顺序表数据缺陷：{kind} 不是非角色条目"),
    };

    /// <summary>
    /// 角色条目 → 行动 / 空槽位；失败时给出原因。死者的空槽位是合法结果，不是失败（D-0013 §1）。
    /// </summary>
    private static (StepSlot? Slot, NightPlanOutcome? Failure) BuildCharacterSlot(
        NightPlanRequest request,
        CharacterId character,
        string tag)
    {
        var owners = request.State.Seats.Where(entry => entry.CharacterValue == character).ToArray();
        if (owners.Length == 0)
        {
            // 角色不在场：空槽位，照样走配额；但记住**这一格是谁的位置**——
            // 麻脸巫婆之类的角色变更能力当夜把该角色创造出来时，这一格会被激活（SlotActivatedEvent）。
            return (StepSlot.Empty(new StepSlotId(tag), character), null);
        }

        if (owners.Length > 1)
        {
            return (null, NightPlanOutcome.Failure(
                "plan.character_duplicated",
                $"角色 {character.Value} 同时出现在多个席位：{string.Join(", ", owners.Select(owner => owner.Seat.Value))}"));
        }

        var actor = owners[0];
        if (actor.LifeValue is null)
        {
            return (null, NightPlanOutcome.Failure(
                "plan.life_unobserved",
                $"席位 {actor.Seat.Value}（{character.Value}）的生死还没有观测，建表不替它猜"));
        }

        if (actor.LifeValue == LifeState.Dead)
        {
            // 已死亡：空槽位，照样走配额；角色同样记在槽位上（复活 / 换角后由进入时求值决定是否唤醒）。
            return (StepSlot.Empty(new StepSlotId(tag), character), null);
        }

        if (request.Actions.Find(character) is not { } action)
        {
            return (null, NightPlanOutcome.Failure(
                "plan.contract_missing",
                $"角色 {character.Value} 的夜间行动契约还没有实现，拒绝把它当成空槽位静默跳过"));
        }

        var prompt = action.BuildPrompt(new NightActionContext
        {
            Actor = actor.Seat,
            Seats = request.Seats,
            State = request.State,
        });

        return (StepSlot.Action(
            new StepSlotId(tag),
            actor.Seat,
            prompt,
            [
                new SeatDependency
                {
                    Seat = actor.Seat,
                    RequiredLife = LifeState.Alive,
                    RequiredCharacter = character,
                },
            ],
            character), null);
    }
}
