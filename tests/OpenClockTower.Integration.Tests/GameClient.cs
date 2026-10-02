using Microsoft.AspNetCore.SignalR.Client;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 测试用客户端：把"连接 + 连接级凭据"绑在一起，像真实前端那样**每条命令都先出示凭据**（D-0012）。
/// </summary>
/// <remarks>
/// 这样既有 89 处 `InvokeAsync("方法", 参数…)` 调用点不必逐个改签名；
/// 负向用例需要"故意出示错凭据"时走 <see cref="InvokeRawAsync"/>（不注入，参数原样发）。
/// </remarks>
public sealed class GameClient : IAsyncDisposable
{
    private readonly HubConnection _connection;

    /// <summary>用一条已 Join 的连接与它的凭据构造。</summary>
    public GameClient(HubConnection connection, string credential)
    {
        _connection = connection;
        Credential = credential;
    }

    /// <summary>本连接的连接级凭据（负向测试要"冒用别人 / 出示旧凭据"时用）。</summary>
    public string Credential { get; }

    /// <summary>裸连接：只给负向测试用（正常用例一律走本类的 InvokeAsync）。</summary>
    public HubConnection Raw => _connection;

    /// <summary>发一条命令：凭据自动作为第一个参数（与服务端 Hub 签名一致）。</summary>
    public Task<T> InvokeAsync<T>(string method, params object?[] args) =>
        _connection.InvokeCoreAsync<T>(method, [Credential, .. args]);

    /// <summary>不注入凭据地发一条调用：负向测试专用（伪造 / 冒用 / 缺凭据）。</summary>
    public Task<T> InvokeRawAsync<T>(string method, params object?[] args) =>
        _connection.InvokeCoreAsync<T>(method, args);

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
