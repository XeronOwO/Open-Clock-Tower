using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 一次**账号注销**在库里抹掉了什么（M5 / G-A1-6）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AffectedTables"/> 是给调用方收尾用的：删掉绑定之后，那些桌内存里的席位名读模型
/// 还留着这个名字，必须重新装载并推给正在看的人——注销不能等到下次重启才生效。
/// </para>
/// <para>
/// 事件流**不在删除面内**：它是这一局的记录，属于所有参与者；而且它里面**没有玩家名**
/// （名字是会话层的读时解析数据，见 D-0021），所以账号一删，公开面上就不再出现这个名字。
/// </para>
/// </remarks>
/// <param name="Deleted">账号行是否真的删掉了（false = 这个账号本来就不存在）。</param>
/// <param name="SeatBindings">删掉的席位绑定行。</param>
/// <param name="OwnedTablesReleased">解除归属的桌数（他开的桌留在库里，但从此没有主持台）。</param>
/// <param name="AffectedTables">席位绑定或归属受影响的桌（收尾刷新读模型用）。</param>
public sealed record AccountErasureResult(
    bool Deleted,
    int SeatBindings,
    int OwnedTablesReleased,
    IReadOnlyList<GameId> AffectedTables);
