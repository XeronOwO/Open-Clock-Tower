using System.Text.Json;
using OpenClockTower.Kernel;

namespace OpenClockTower.Server;

/// <summary>
/// 领域事件的持久化形状：**类型名 + JSON 载荷**。
/// </summary>
/// <remarks>
/// 内核不认识任何序列化框架（架构 §1），所以映射只在这一层做；反序列化按程序集里的类型名查表，
/// 未知类型**显式失败**——这既是重建的失败原因，也是"不静默继续"的保证。
/// </remarks>
public static class GameEventSerialization
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>取类型名（持久化用）。</summary>
    public static string TypeNameOf(GameEvent gameEvent) => gameEvent.GetType().Name;

    /// <summary>序列化事件载荷。</summary>
    public static string Serialize(GameEvent gameEvent) =>
        JsonSerializer.Serialize(gameEvent, gameEvent.GetType(), Options);

    /// <summary>按类型名反序列化事件载荷；未知类型显式抛出。</summary>
    public static GameEvent Deserialize(string typeName, string payload)
    {
        var type = typeof(GameEvent).Assembly
            .GetTypes()
            .FirstOrDefault(candidate =>
                !candidate.IsAbstract
                && typeof(GameEvent).IsAssignableFrom(candidate)
                && string.Equals(candidate.Name, typeName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"未知事件类型：{typeName}");

        try
        {
            return JsonSerializer.Deserialize(payload, type, Options) as GameEvent
                ?? throw new InvalidOperationException($"事件反序列化失败：{typeName}");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"事件载荷损坏：{typeName}", exception);
        }
    }

    /// <summary>序列化步骤机状态快照；null 状态序列化为 null。</summary>
    public static string? SerializeState(StepMachineState? machine) =>
        machine is null ? null : JsonSerializer.Serialize(machine, Options);

    /// <summary>反序列化步骤机状态快照；损坏时显式抛错。</summary>
    public static StepMachineState? DeserializeState(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StepMachineState>(payload, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("快照载荷损坏", exception);
        }
    }
}
