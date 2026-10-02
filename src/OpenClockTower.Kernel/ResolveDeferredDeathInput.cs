namespace OpenClockTower.Kernel;

/// <summary>
/// 裁定一条待定死亡：确认（该玩家死亡）或阻止（免死）——麻脸巫婆之夜的说书人输入。
/// </summary>
/// <remarks>依据 <c>docs/standard/rulings.md</c> R-0030 第 2 条；窗口关闭后该输入被拒。</remarks>
public sealed record ResolveDeferredDeathInput : StepMachineInput
{
    /// <summary>待定死亡的目标席位。</summary>
    public required SeatId Target { get; init; }

    /// <summary>true = 确认死亡；false = 阻止死亡。</summary>
    public required bool Killed { get; init; }

    /// <summary>说书人的说明（可空）。</summary>
    public string? Note { get; init; }
}
