using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>一条候选事实在**此刻账上**的求值结论：要念给玩家的那句话 + 它的真值（R-0057-C）。</summary>
internal sealed record SavantFactEvaluation
{
    /// <summary>人话文案（说书人选中它时，玩家看到的就是这一句）。</summary>
    public required string Text { get; init; }

    /// <summary>这句话此刻是对是错。</summary>
    public required OptionTruth Truth { get; init; }

    /// <summary>按一个布尔判断造结论：成立为真、否则为假（文案由调用方给）。</summary>
    internal static SavantFactEvaluation Of(bool holds, string text) =>
        new() { Text = text, Truth = holds ? OptionTruth.True : OptionTruth.False };
}
