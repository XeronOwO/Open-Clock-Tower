using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 建表：按剧本的完整夜晚顺序表与当前状态账，展开今晚的步骤序列。
/// </summary>
/// <remarks>
/// <para>
/// 依据 D-0013 §1：步骤表按**剧本的完整夜晚顺序表**展开，不按在场角色展开——
/// 角色不在场 / 已死亡 → 生成空槽位（<see cref="StepSlotKind.Empty"/>），照样走完配额。
/// 顺序表上的触发格（理发师）→ 触发槽位（<see cref="StepSlotKind.Trigger"/>）：进入时只标记时机，
/// 是否开请求由触发管线按步骤机事实决定，因此**不要求行动契约**。
/// 非角色条目（黄昏 / 爪牙信息 / 恶魔信息 / 信息环节开始 / 黎明）→ 节拍或黎明等待槽位。
/// </para>
/// <para>
/// **不猜**：席位没有角色、在场角色的生死未观测、行动契约未实现、同一角色被**多名存活玩家**持有，
/// 一律返回失败结果，由调用方拒绝开夜；禁止把"不知道"映射成默认值（D-0015 的同一原则）。
/// 已死亡玩家的角色标记仍留在魔典上（角色唯一只约束存活持有者）——
/// 方古侵染外来者造成的「已死亡原方古 + 存活新方古」两个同名标记即由此而来（R-0034）。
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

            if (entry.Kind == NightOrderEntryKind.CharacterTrigger)
            {
                if (entry.Character is not { } triggerCharacter)
                {
                    throw new InvalidOperationException("夜晚顺序表数据缺陷：角色条目没有角色");
                }

                var (triggerSlot, triggerFailure) = BuildTriggerSlot(request, triggerCharacter, tag);
                if (triggerFailure is not null)
                {
                    return triggerFailure;
                }

                slots.Add(triggerSlot!);
                continue;
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

            var (slot, failure) = BuildCharacterSlot(request, character, tag, phase);
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
        NightOrderEntryKind.CharacterAction or NightOrderEntryKind.CharacterTrigger => entry.Character is { } character
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
        string tag,
        GamePhase phase)
    {
        var binding = PhilosopherBinding.Of(request.State);

        // 哲学家的「获得能力」（R-0036 第 1 条）：被获得角色的格**没有行动者**时，这一格交给获得者代行
        // ——当夜就能用上，首夜能力因此也照此落地。判定与结算期同源（PhilosopherBinding）。
        if (binding is { } grant
            && character == grant.Granted
            && PhilosopherBinding.GrantedSlotIsFree(request.State, grant.Granted, phase, request.Variant)
            && BuildGrantedSlot(request, grant, tag) is { } takeover)
        {
            return (takeover, null);
        }

        var owners = request.State.Seats.Where(entry => entry.CharacterValue == character).ToArray();
        if (owners.Length == 0)
        {
            // 角色不在场：空槽位，照样走配额；但记住**这一格是谁的位置**——
            // 麻脸巫婆之类的角色变更能力当夜把该角色创造出来时，这一格会被激活（SlotActivatedEvent）。
            return (StepSlot.Empty(new StepSlotId(tag), character), null);
        }

        // 生死未观测的持有者：不猜（D-0015）。任何一名持有者的生死未观测都拒绝——
        // 否则"他是不是也活着"无从判定，角色归属就不唯一。
        var unobserved = owners.FirstOrDefault(owner => owner.LifeValue is null);
        if (unobserved is not null)
        {
            return (null, NightPlanOutcome.Failure(
                "plan.life_unobserved",
                $"席位 {unobserved.Seat.Value}（{character.Value}）的生死还没有观测，建表不替它猜"));
        }

        // 「角色唯一」的规范对象是**存活的持有者**：已死亡玩家的角色标记仍留在魔典上
        // （百科《理发师》· 2026-10-01 抓取 · 范例：角色可以落到已死亡的玩家身上；R-0029 口径同族）。
        // 方古侵染外来者会产生「已死亡的原方古 + 存活的新方古」两个同名标记（R-0034）——
        // 这一格属于存活的那一位；**多名存活持有者**才是真正的数据缺陷。
        var aliveOwners = owners.Where(owner => owner.LifeValue == LifeState.Alive).ToArray();
        if (aliveOwners.Length > 1)
        {
            return (null, NightPlanOutcome.Failure(
                "plan.character_duplicated",
                $"角色 {character.Value} 同时被多名存活玩家持有："
                + string.Join(", ", aliveOwners.Select(owner => owner.Seat.Value))));
        }

        if (aliveOwners.Length == 0)
        {
            // 全部持有者都已死亡：空槽位，照样走配额（复活 / 换角后由进入时求值决定是否唤醒）。
            return (StepSlot.Empty(new StepSlotId(tag), character), null);
        }

        var actor = aliveOwners[0];

        // 女裁缝的「每局限一次」已经用掉（含醉酒 / 中毒时使用）：不再唤醒她——
        // 「为她放置"失去能力"提示标记，并从夜晚顺序表上移除她的夜晚标记」（百科《女裁缝》· 运作方式 6；
        // 平台口径 R-0040；与哲学家的「机会已浪费」同族，R-0036 第 2 条）。
        if (character == SeamstressNightAction.Seamstress
            && request.State.AbilityUses.WasUsed(actor.Seat, SeamstressNightAction.InfoAbility))
        {
            return (NoActionSlot(
                tag,
                actor.Seat,
                character,
                "女裁缝的「每局限一次」已经用掉：本局不再被唤醒（百科《女裁缝》· 运作方式 6；R-0040）"), null);
        }

        if (character == PhilosopherAbility.Character
            && BuildPhilosopherSlot(request, binding, actor.Seat, tag, phase) is { } philosopherSlot)
        {
            // 已经获得能力（或那次获得被浪费掉）：他自己的格不再开出"选择"。
            return (philosopherSlot, null);
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
            LastDay = request.LastDay,
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

    /// <summary>
    /// 哲学家自己的格（R-0036 第 2、3 条）：已经获得能力时，这一格要么**代行**获得的能力
    /// （被获得角色的格归它的持有者），要么本夜无行动；还没获得过（也没被浪费掉）时返回 null——
    /// 走常规的「选择要获得谁的能力」路径。
    /// </summary>
    private static StepSlot? BuildPhilosopherSlot(
        NightPlanRequest request,
        (SeatId Philosopher, CharacterId Granted)? binding,
        SeatId actor,
        string tag,
        GamePhase phase)
    {
        if (binding is not { } grant)
        {
            if (request.State.AbilityUses.WasUsed(actor, PhilosopherAbility.GrantAbility))
            {
                // 「限次能力在醉酒 / 中毒期间被使用 = 已浪费」：不能再获得能力（百科《重要细节》三-3）。
                return NoActionSlot(
                    tag,
                    actor,
                    PhilosopherAbility.Character,
                    "哲学家的「每局限一次」已经用掉（当时能力未生效，机会被浪费）：本局不能再获得能力");
            }

            return null;
        }

        var free = PhilosopherBinding.GrantedSlotIsFree(request.State, grant.Granted, phase, request.Variant);
        if (!free && PhilosopherBinding.HasActionOnPhase(grant.Granted, phase, request.Variant)
            && BuildGrantedSlot(request, grant, tag) is { } delegated)
        {
            // 被获得角色的格归它的持有者（醉酒 → 能力不生效）：获得者改在自己的格上代行。
            return delegated;
        }

        return NoActionSlot(
            tag,
            actor,
            PhilosopherAbility.Character,
            free
                ? $"本夜「{grant.Granted.Value}」的格由哲学家代行（那一格没有行动者）：他自己的格不产生行动"
                : $"获得的能力（{grant.Granted.Value}）本夜没有可执行的行动"
                    + "（触发型能力 / 不在本阶段顺序表上 / 夜间契约未实现）：本格不产生行动");
    }

    /// <summary>
    /// 构造「代行」槽位：能力属于被获得的角色（结算契约的检索键），行动者是获得者（R-0036）。
    /// 契约没实现时返回 null——由调用方决定退回空槽位还是"本夜无行动"。
    /// </summary>
    private static StepSlot? BuildGrantedSlot(
        NightPlanRequest request,
        (SeatId Philosopher, CharacterId Granted) grant,
        string tag)
    {
        if (request.Actions.Find(grant.Granted) is not { } action)
        {
            return null;
        }

        var prompt = action.BuildPrompt(new NightActionContext
        {
            Actor = grant.Philosopher,
            Seats = request.Seats,
            State = request.State,
            LastDay = request.LastDay,
        });

        return StepSlot.GrantedAction(
            new StepSlotId(tag),
            grant.Philosopher,
            PhilosopherAbility.Character,
            prompt,
            [
                new SeatDependency
                {
                    Seat = grant.Philosopher,
                    RequiredLife = LifeState.Alive,
                    RequiredCharacter = PhilosopherAbility.Character,
                },
            ],
            grant.Granted);
    }

    /// <summary>
    /// 「本夜无行动」槽位：仍有行动者与依赖（进入时按账确认他还站得住），但没有合法选项——
    /// 按声明的 Skip 走，配额照走，并在事件流里留一条可归因的跳过记录（R-0009）。
    /// 哲学家（机会已浪费，R-0036）与女裁缝（用过即失去能力，R-0040）共用。
    /// </summary>
    private static StepSlot NoActionSlot(string tag, SeatId actor, CharacterId character, string reason) =>
        StepSlot.Action(
            new StepSlotId(tag),
            actor,
            new ChoicePrompt
            {
                Context = reason,
                Options = [],
                OnNoOption = NoOptionBehavior.Skip,
            },
            [
                new SeatDependency
                {
                    Seat = actor,
                    RequiredLife = LifeState.Alive,
                    RequiredCharacter = character,
                },
            ],
            character);

    /// <summary>
    /// 角色触发格 → 触发槽位：与行动槽位同款完整性校验（席位缺角色 / 生死未观测 / 角色重复一律拒绝，
    /// 不猜），但**不要求夜间行动契约**——这一格承载的是死亡触发的能力（理发师），
    /// 不是持有者本人的行动；本人在场也不行动。
    /// </summary>
    private static (StepSlot? Slot, NightPlanOutcome? Failure) BuildTriggerSlot(
        NightPlanRequest request,
        CharacterId character,
        string tag)
    {
        var owners = request.State.Seats.Where(entry => entry.CharacterValue == character).ToArray();

        // 与行动槽位同一把尺子：生死未观测的持有者一律拒绝（不猜）。
        var unobserved = owners.FirstOrDefault(owner => owner.LifeValue is null);
        if (unobserved is not null)
        {
            return (null, NightPlanOutcome.Failure(
                "plan.life_unobserved",
                $"席位 {unobserved.Seat.Value}（{character.Value}）的生死还没有观测，建表不替它猜"));
        }

        // 「角色唯一」只约束存活持有者：已死亡玩家的标记仍在魔典上（与行动槽位的口径一致）。
        var aliveOwners = owners.Where(owner => owner.LifeValue == LifeState.Alive).ToArray();
        if (aliveOwners.Length > 1)
        {
            return (null, NightPlanOutcome.Failure(
                "plan.character_duplicated",
                $"角色 {character.Value} 同时被多名存活玩家持有："
                + string.Join(", ", aliveOwners.Select(owner => owner.Seat.Value))));
        }

        return (StepSlot.Trigger(new StepSlotId(tag), character), null);
    }
}
