using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>候选事实库里的一条候选：稳定编码 + 人话文案 + 真值 + 呈现元数据（R-0057-C）。</summary>
internal sealed record SavantFactCandidate
{
    /// <summary>进裁定文本的稳定编码（<c>fact:编码[:参数]</c>；不含 <c>|</c>）。</summary>
    public required string Value { get; init; }

    /// <summary>事实编码。</summary>
    public required string Code { get; init; }

    /// <summary>参数取值；无参数事实为 null。</summary>
    public string? Parameter { get; init; }

    /// <summary>人话文案。</summary>
    public required string Text { get; init; }

    /// <summary>候选分组（说书人端分栏）。</summary>
    public required string Group { get; init; }

    /// <summary>真值。</summary>
    public required OptionTruth Truth { get; init; }

    /// <summary>是不是点名类高强度信息（平台只标出来，用不用由说书人裁量）。</summary>
    public bool HighIntensity { get; init; }

    /// <summary>互斥组：同组不同取值互为反面（奇 / 偶一类）；组合防呆用。</summary>
    public string? OppositeGroup { get; init; }
}
