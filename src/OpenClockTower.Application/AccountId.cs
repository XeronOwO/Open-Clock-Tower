using System.Globalization;

namespace OpenClockTower.Application;

/// <summary>账号标识：跨局稳定、由账号表签发（D-0021）。</summary>
/// <param name="Value">签发序号（从 1 开始）。</param>
public readonly record struct AccountId(int Value)
{
    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
