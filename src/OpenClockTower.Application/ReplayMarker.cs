using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 复盘步骤在圆盘上的可视化标记；<see cref="Kind"/> 取值是术语表登记的 slug
/// （击杀箭头 <c>kill-arrow</c> / 死亡帷幕 <c>shroud</c> / 换角 <c>character-change</c> /
/// 换手 <c>role-rebind</c> / 中毒 <c>poisoned</c> / 醉酒 <c>drunk</c> /
/// 加入 <c>traveller-joined</c> / 离场 <c>traveller-departed</c> / 流放 <c>exile</c> /
/// 保护 <c>protected</c> / 额外提名 <c>extra-nomination</c> / 重获能力 <c>regained-ability</c> /
/// 效果窗口 <c>effect-window</c>）。
/// </summary>
/// <remarks>
/// 标记只描述"这一步在圆盘上怎么呈现"，不承担事实：事实由 <see cref="ReplaySeatDelta"/>
/// 与步骤文案承载；新增标记必须先登记 `docs/standard/terminology.md` 再落代码（D-0020 第 5 条）。
/// </remarks>
public sealed record ReplayMarker
{
    /// <summary>标记类别 slug（术语表口径）。</summary>
    public required string Kind { get; init; }

    /// <summary>标记落在哪个席位；无席位的标记为 null。</summary>
    public SeatId? Seat { get; init; }

    /// <summary>箭头类标记的起点（如恶魔击杀的发动席位）。</summary>
    public SeatId? From { get; init; }

    /// <summary>箭头类标记的终点。</summary>
    public SeatId? To { get; init; }

    /// <summary>可选的补充文本（如「原行动者 3 号 → 5 号」）。</summary>
    public string? Text { get; init; }
}
