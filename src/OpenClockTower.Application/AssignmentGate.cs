using OpenClockTower.Kernel;
using OpenClockTower.Rules;
using static OpenClockTower.Application.GateRejections;

namespace OpenClockTower.Application;

/// <summary>
/// 开局分配（<see cref="AssignCharactersCommand"/>）的合法性：席位属于本局、角色在首版花名册里、
/// **旅行者不走这条路径**、同批不重复（角色唯一）。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁）：一个命令族的规则集中在一处，
/// 身份 / 阶段 / 幂等仍由管线负责。旅行者的初始阵营由说书人私下裁定（D-0022 / R-0044），
/// 加入走专属流程（D1），因此这里显式拒绝——而不是让分派层的类型推导抛异常。
/// </remarks>
internal static class AssignmentGate
{
    /// <summary>检查一批分配；返回 null = 通过。</summary>
    public static CommandRejection? Check(AssignCharactersCommand command, GameSetup? setup)
    {
        if (command.Assignments.Count == 0)
        {
            return Reject("legality.assignment_empty", "开局分配至少要给出一名席位的角色", "legality");
        }

        if (setup is null)
        {
            return Reject("legality.setup_missing", "本局还没有会话信息（席位名单）", "legality");
        }

        var seats = new HashSet<SeatId>();
        var characters = new HashSet<CharacterId>();
        foreach (var assignment in command.Assignments)
        {
            var seat = SeatGate.CheckExists(assignment.Seat, setup);
            if (seat is not null)
            {
                return seat;
            }

            if (!seats.Add(assignment.Seat))
            {
                return Reject(
                    "legality.seat_duplicated",
                    $"同一批分配里席位 {assignment.Seat.Value} 出现了多次",
                    "legality");
            }

            if (!SectsAndVioletsRoster.Contains(assignment.Character))
            {
                return Reject(
                    "legality.character_unknown",
                    $"角色 {assignment.Character.Value} 不是《梦殒春宵》首版角色",
                    "legality");
            }

            // 旅行者的初始阵营不由角色类型推导（说书人私下裁定，D-0022 / R-0044）：
            // 开局分配只会写「类型对应阵营」的初始状态——这里显式拒绝，而不是让分派层抛异常（D1）。
            if (SectsAndVioletsRoster.TypeOf(assignment.Character) == CharacterType.Traveller)
            {
                return Reject(
                    "legality.character_not_assignable",
                    $"旅行者 {assignment.Character.Value} 不走开局分配：开局分配只覆盖镇民 / 外来者 / 爪牙 / 恶魔，"
                    + "旅行者的加入走专属流程（D-0022 / D1）",
                    "legality");
            }

            if (!characters.Add(assignment.Character))
            {
                return Reject(
                    "legality.character_duplicated",
                    $"同一批分配里角色 {assignment.Character.Value} 出现了多次（角色唯一）",
                    "legality");
            }
        }

        return null;
    }
}
