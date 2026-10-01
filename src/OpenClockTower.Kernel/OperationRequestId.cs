namespace OpenClockTower.Kernel;

/// <summary>
/// 操作请求标识：稳定标识，进事件流后永不改变。
/// </summary>
/// <param name="Value">稳定标识（由计划标识与槽位标识确定性导出）。</param>
public readonly record struct OperationRequestId(string Value)
{
    /// <inheritdoc />
    public override string ToString() => Value;
}
