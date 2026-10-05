namespace OpenClockTower.Application;

/// <summary>
/// 玩家（博学者）在白天私下向说书人要两条信息（一真一假，R-0057）。
/// </summary>
/// <remarks>
/// 与操作请求方向相反：这条命令由玩家主动发出。席位**不在命令里自称**——由凭据推导
/// （D-0012：客户端声明一律不可信）。命令没有参数：内容完全由说书人给（百科《博学者》· 角色能力）。
/// </remarks>
public sealed record AskSavantQuestionCommand : GameCommand;
