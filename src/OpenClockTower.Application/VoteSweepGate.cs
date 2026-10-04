using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 钟盘收票命令的闸（R-0017 目标形态）：身份与合法性的形状检查。
/// </summary>
/// <remarks>
/// 从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁，与 <see cref="ArtistQuestionGate"/> 同款）：
/// 这里只回答"这条命令由谁发、参数形状对不对"；"现在能不能收"由内核按白天账判。
/// </remarks>
internal static class VoteSweepGate
{
    /// <summary>身份闸：开始 / 继续由说书人掌握，逐席到点只认系统节拍器。</summary>
    internal static CommandRejection? IdentityRejection(GameCommand command, Actor actor) =>
        command switch
        {
            StartVoteSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            StartVoteSweepCommand => Reject("identity", "identity.storyteller_only", "只有说书人或宿主可以开始收票"),

            ResumeVoteSweepCommand when actor.Kind is ActorKind.Host or ActorKind.Storyteller => null,
            ResumeVoteSweepCommand => Reject("identity", "identity.storyteller_only", "只有说书人或宿主可以继续收票"),

            CollectSeatVoteCommand when actor.Kind == ActorKind.System => null,
            CollectSeatVoteCommand => Reject("identity", "identity.system_only", "收票到点输入只能由系统节拍器发出"),

            _ => Reject("identity", "identity.unknown_sweep_command", "不是钟盘收票命令（防御性兜底）"),
        };

    /// <summary>合法性闸：参数范围与席位名单的形状检查（与内核同尺 <see cref="VoteSweepLimits"/>）。</summary>
    internal static CommandRejection? LegalityRejection(GameCommand command, GameSetup? setup) =>
        command switch
        {
            StartVoteSweepCommand startSweep when !VoteSweepLimits.IsCountdownValid(startSweep.CountdownMilliseconds)
                => Reject(
                    "legality",
                    "legality.sweep_countdown_invalid",
                    $"倒计时必须在 {VoteSweepLimits.MinCountdownMilliseconds}–{VoteSweepLimits.MaxCountdownMilliseconds} 毫秒之间"),
            StartVoteSweepCommand startSweep when !VoteSweepLimits.IsIntervalValid(startSweep.IntervalMilliseconds)
                => Reject(
                    "legality",
                    "legality.sweep_interval_invalid",
                    $"逐席间隔必须在 {VoteSweepLimits.MinIntervalMilliseconds}–{VoteSweepLimits.MaxIntervalMilliseconds} 毫秒之间"),
            StartVoteSweepCommand startSweep => CheckNominationIndex(startSweep.NominationIndex),
            ResumeVoteSweepCommand resumeSweep => CheckNominationIndex(resumeSweep.NominationIndex),
            CollectSeatVoteCommand collectSeat => CheckNominationIndex(collectSeat.NominationIndex)
                ?? CheckSeatExists(collectSeat.Seat, setup),
            _ => Reject("legality", "legality.unknown_sweep_command", "不是钟盘收票命令（防御性兜底）"),
        };

    private static CommandRejection? CheckNominationIndex(int index) =>
        index < 1
            ? Reject("legality", "legality.nomination_index_invalid", $"提名序号必须从 1 开始：{index}")
            : null;

    private static CommandRejection? CheckSeatExists(SeatId seat, GameSetup? setup)
    {
        if (setup is null)
        {
            return Reject("legality", "legality.setup_missing", "本局还没有会话信息（席位名单）");
        }

        return setup.Seats.Any(item => item.Seat == seat)
            ? null
            : Reject("legality", "legality.seat_unknown", $"席位 {seat.Value} 不在本局席位名单里");
    }

    private static CommandRejection Reject(string gate, string code, string message) =>
        new() { Code = code, Message = message, Gate = gate };
}
