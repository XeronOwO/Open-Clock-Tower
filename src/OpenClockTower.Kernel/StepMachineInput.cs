namespace OpenClockTower.Kernel;

/// <summary>
/// 步骤机的输入：所有会改变步骤机状态的外部动作。
/// </summary>
/// <remarks>
/// 依据 D-0008：内核只接收输入、产出事件；时间与 IO 一律由上层负责（例如配额走完、
/// 座位状态变化都是"输入"，不是内核自己感知的）。
/// </remarks>
public abstract record StepMachineInput;
