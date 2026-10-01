namespace OpenClockTower.Application;

/// <summary>一条命令的处理结果类别。</summary>
public enum CommandResultKind
{
    /// <summary>已接受：事件已提交，状态已推进。</summary>
    Accepted,

    /// <summary>被拒绝：状态不变，原因见 <see cref="CommandResult.Rejection"/>。</summary>
    Rejected,

    /// <summary>重复投递：按幂等键返回首次结果（不重复生效）。</summary>
    Duplicate,

    /// <summary>处理失败（内部异常 / 重建失败）：状态不变，必须显式报错。</summary>
    Failed,
}
