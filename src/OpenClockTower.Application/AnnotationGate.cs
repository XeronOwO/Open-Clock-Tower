using OpenClockTower.Kernel;
using static OpenClockTower.Application.GateRejections;

namespace OpenClockTower.Application;

/// <summary>
/// 说书人注记（D-0019）的合法性：席位属于本局、文本合规、每席不超上限；改 / 删必须先存在
/// （已删除的标识不再接受）。
/// </summary>
/// <remarks>从 <see cref="CommandGatePipeline"/> 拆出（单文件 600 行门禁）：一个命令族的规则集中在一处。</remarks>
internal static class AnnotationGate
{
    /// <summary>加注记：席位属于本局、文本合规、每席不超上限。</summary>
    public static CommandRejection? CheckAdd(
        AddSeatAnnotationCommand command,
        GameSetup? setup,
        SeatAnnotationLedger annotations)
    {
        var seat = SeatGate.CheckExists(command.Seat, setup);
        if (seat is not null)
        {
            return seat;
        }

        var text = CheckText(command.Text);
        if (text is not null)
        {
            return text;
        }

        return annotations.CountOn(command.Seat) >= SeatAnnotationText.MaxPerSeat
            ? Reject(
                "legality.annotation_limit",
                $"席位 {command.Seat.Value} 的注记已达上限（每席最多 {SeatAnnotationText.MaxPerSeat} 条）",
                "legality")
            : null;
    }

    /// <summary>改 / 删注记的合法性：注记必须还存在（已删除的标识不再接受）。</summary>
    public static CommandRejection? CheckTarget(SeatAnnotationId id, SeatAnnotationLedger annotations) =>
        annotations.Find(id) is null
            ? Reject("legality.annotation_unknown", $"注记 {id} 不存在（可能已被删除）", "legality")
            : null;

    /// <summary>
    /// 注记文本的合法性（D-0019）：归一化后非空、不超长、不含控制字符。
    /// 归一化本身在分派时做（同一把尺子 <see cref="SeatAnnotationText.TryNormalize"/>）。
    /// </summary>
    public static CommandRejection? CheckText(string? raw)
    {
        if (SeatAnnotationText.TryNormalize(raw, out _, out var failure))
        {
            return null;
        }

        return failure switch
        {
            "empty" => Reject("legality.annotation_empty", "注记不能为空", "legality"),
            "too_long" => Reject(
                "legality.annotation_too_long",
                $"注记最多 {SeatAnnotationText.MaxLength} 个字符",
                "legality"),
            _ => Reject("legality.annotation_control", "注记不能包含控制字符", "legality"),
        };
    }
}
