namespace OpenClockTower.Application;

/// <summary>
/// 四道闸共用的拒绝构造：机器码 + 中文说明 + 闸名（D-0012 §4.2）。
/// </summary>
/// <remarks>
/// 拒绝检查按域拆在各自的门里（开局分配 / 说书人注记 / 钟盘收票…），但拒绝的**形状**只在这一处定义，
/// 避免每个门各写一份；<see cref="CommandRejection"/> 保持纯数据。
/// </remarks>
internal static class GateRejections
{
    /// <summary>构造一条拒绝（码 / 人话 / 闸名）。</summary>
    public static CommandRejection Reject(string code, string message, string gate) =>
        new() { Code = code, Message = message, Gate = gate };
}
