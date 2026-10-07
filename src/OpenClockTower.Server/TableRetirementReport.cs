using System.Globalization;
using OpenClockTower.Application;

namespace OpenClockTower.Server;

/// <summary>
/// 一轮空闲桌清扫的读数（M5 / G-A6-5）：逐桌判定 + 汇总。
/// </summary>
/// <remarks>
/// 维护命令（<c>retire-tables</c>）与定时清扫共用这同一个读数，于是"人肉看到的"与
/// "自动做掉的"是同一件事的两种触发方式，不会出现两套口径。
/// </remarks>
/// <param name="Applied">是否真的动了库（false = 只报告）。</param>
/// <param name="Now">这一轮用的"现在"（时钟从宿主注入，报告里写出来才能复核阈值的算法）。</param>
/// <param name="Outcomes">逐桌判定（按桌标识升序，与读数同序）。</param>
public sealed record TableRetirementReport(
    bool Applied,
    DateTimeOffset Now,
    IReadOnlyList<TableRetirementOutcome> Outcomes)
{
    /// <summary>到期该回收的桌数（含已回收的）。</summary>
    public int DueCount => Outcomes.Count(item => item.Verdict == TableRetirementVerdict.Retire);

    /// <summary>本轮真的回收掉的桌数。</summary>
    public int RetiredCount => Outcomes.Count(item => item.Purged is not null);

    /// <summary>因有在线连接而跳过的桌数。</summary>
    public int InUseCount => Outcomes.Count(item => item.Verdict == TableRetirementVerdict.InUse);

    /// <summary>无法判定（没有时间依据）的桌数。</summary>
    public int UndecidableCount => Outcomes.Count(item => item.Verdict == TableRetirementVerdict.Undecidable);

    /// <summary>本轮删掉的总行数（不含顺手清掉的孤儿）。</summary>
    public int DeletedRows => Outcomes.Sum(item => item.Purged?.TotalRows ?? 0);

    /// <summary>顺手清掉的孤儿行总数。</summary>
    public int OrphanRows => Outcomes.Sum(item => item.Purged?.OrphanRows ?? 0);

    /// <summary>把人看的读数写成多行文本（维护命令与日志共用同一种表述）。</summary>
    public string Describe()
    {
        var lines = new List<string>
        {
            $"清扫：时刻={Now:yyyy-MM-dd HH:mm:ss}Z · 在册={Outcomes.Count} · 到期={DueCount}"
            + $" · 已回收={RetiredCount} · 在线跳过={InUseCount} · 无法判定={UndecidableCount}"
            + $" · {(Applied ? "已执行" : "只报告（--apply 才真的删）")}",
        };

        if (RetiredCount > 0)
        {
            lines.Add(
                $"本轮删除：行数={DeletedRows.ToString(CultureInfo.InvariantCulture)}"
                + $"（含顺手清掉的孤儿 {OrphanRows.ToString(CultureInfo.InvariantCulture)} 行）");
        }

        foreach (var outcome in Outcomes)
        {
            lines.Add(
                $"某一桌：标识={outcome.GameId.Value} · 桌名={(outcome.Name.Length == 0 ? "(未命名)" : outcome.Name)}"
                + $" · 判定={VerdictText(outcome.Verdict)} · {outcome.Reason}"
                + (outcome.Purged is { } purged
                    ? $" · 已删：事件={purged.Events} 快照={purged.Snapshots} 回执={purged.Receipts}"
                      + $" 绑定={purged.SeatBindings} 目录={purged.Games}"
                    : string.Empty));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string VerdictText(TableRetirementVerdict verdict) => verdict switch
    {
        TableRetirementVerdict.Retire => "回收",
        TableRetirementVerdict.InUse => "在用",
        TableRetirementVerdict.Undecidable => "无法判定",
        _ => "未到期",
    };
}
