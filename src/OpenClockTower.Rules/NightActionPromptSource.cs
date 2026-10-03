using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 说书人裁定类提示的实时重建：把内核的 <see cref="SlotPromptRequest"/> 翻成角色契约的
/// <see cref="NightActionContext"/>，按**能力归属角色**取契约重建提示。
/// </summary>
/// <remarks>
/// 与结算同一把检索键（<see cref="SlotPromptRequest.Character"/> 来自槽位的 <c>Owner</c>）：
/// 代行槽位上是「被获得角色」，避免"结算找得到、重建找不到"。角色没有契约时返回 null，
/// 调用方退回计划快照。
/// </remarks>
internal sealed class NightActionPromptSource : ISlotPromptSource
{
    private readonly INightActionCatalog _actions;

    internal NightActionPromptSource(INightActionCatalog actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        _actions = actions;
    }

    /// <inheritdoc />
    public ChoicePrompt? Rebuild(SlotPromptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _actions.Find(request.Character)?.BuildPrompt(new NightActionContext
        {
            Actor = request.Actor,
            Seats = request.Seats,
            State = request.State,
            LastDay = request.LastDay,
        });
    }
}
