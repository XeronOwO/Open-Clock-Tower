using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;
using OpenClockTower.Server;

var builder = WebApplication.CreateBuilder(args);
var serverOptions = builder.Configuration
    .GetSection(GameServerOptions.SectionName)
    .Get<GameServerOptions>() ?? new GameServerOptions();

builder.Services.Configure<GameServerOptions>(
    builder.Configuration.GetSection(GameServerOptions.SectionName));
builder.Services.AddSingleton(new PacingOptions
{
    SlotQuota = TimeSpan.FromSeconds(serverOptions.SlotQuotaSeconds),
});
builder.Services.AddSingleton<IClock, SystemClock>();
// GameId 是值对象（record struct），用非泛型重载注册实例
builder.Services.AddSingleton(typeof(GameId), new GameId(serverOptions.GameId));
builder.Services.AddDbContextFactory<GameDbContext>(options => options.UseSqlite(
    $"Data Source={Path.Combine(builder.Environment.ContentRootPath, serverOptions.DatabasePath)}"));
builder.Services.AddSingleton<IGameStore, EfGameStore>();
builder.Services.AddSingleton<IGameCatalog, EfGameCatalog>();
// 账号与席位绑定（D-0021）：账号是全局身份、绑定是会话信息；口令 / 会话凭据只存哈希（SecretToken）。
builder.Services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
builder.Services.AddSingleton<IAccountStore, EfAccountStore>();
builder.Services.AddSingleton<ISeatBindingStore, EfSeatBindingStore>();
builder.Services.AddSingleton<AccountService>();
builder.Services.AddSingleton<SeatBindingService>();
builder.Services.AddSingleton<AccountSessionRegistry>();
// 管理员名单（D-0025：只有管理员能开桌）：来自本机配置的登录名清单；清单为空 = 谁都不是管理员。
builder.Services.AddSingleton(provider => new AdminDirectory(builder.Configuration));
// 大厅用例（D-0025）：列桌 / 建桌。
builder.Services.AddSingleton<LobbyService>();
// 规则层的角色契约：提示目录与结算目录指向同一批实现（NightActions），常驻效果来源单列。
builder.Services.AddSingleton<IAbilityResolutionCatalog>(NightActions.Resolutions);
builder.Services.AddSingleton<IReadOnlyList<IStandingEffectSource>>(NightActions.StandingEffects);
// 多桌（D-0024）：局注册表是"一局"这一层的**组合根**——按 GameId 造出会话 + 席位名读模型 + 复盘读侧
// 并各自从事件流恢复。会话与读模型**不再是进程级单例**：它们每局一份，由 GameHub 按连接解析、
// 由启动引导装载全部在册的桌。
builder.Services.AddSingleton(provider => new GameRegistry(
    provider.GetRequiredService<IGameStore>(),
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<ISeatBindingStore>(),
    provider.GetRequiredService<IAccountStore>(),
    provider.GetRequiredService<IAbilityResolutionCatalog>(),
    provider.GetRequiredService<IReadOnlyList<IStandingEffectSource>>(),
    provider.GetRequiredService<IClock>(),
    provider.GetRequiredService<PacingOptions>(),
    provider.GetRequiredService<ILoggerFactory>(),
    provider.GetRequiredService<GameId>()));
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<HubActorResolver>();
builder.Services.AddSingleton<NotificationDispatcher>();
// 连接 ↔ 桌的绑定（多桌 D-0024）：单例——SignalR 的 Hub 每次调用新建实例，字段记不住东西。
builder.Services.AddSingleton(provider => new HubGameScope(
    provider.GetRequiredService<GameRegistry>(),
    provider.GetRequiredService<GameId>(),
    provider.GetRequiredService<NotificationDispatcher>()));
// 加入入口（玩家与说书人两侧；自己解析所在桌，单例、无状态协作者）。
builder.Services.AddSingleton(provider => new HubJoinScope(
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<HubGameScope>(),
    provider.GetRequiredService<SeatJoinCoordinator>(),
    provider.GetRequiredService<ConnectionRegistry>(),
    provider.GetRequiredService<NotificationDispatcher>(),
    provider.GetRequiredService<ILogger<GameHub>>()));
// 桌务（锁桌 / 解除席位绑定）：单例、无状态协作者。
builder.Services.AddSingleton(provider => new HubTableAdmin(
    provider.GetRequiredService<LobbyService>(),
    provider.GetRequiredService<SeatJoinCoordinator>(),
    provider.GetRequiredService<NotificationDispatcher>()));

// 加入 / 认领的席位定位与凭据签发（D-0021）：从 GameHub 拆出（单文件 600 行门禁）。
// 多桌（D-0024）：本类不持有"当前是哪一局"，局面由调用方按次传入，所以这里没有 GameId / GameSession 依赖。
builder.Services.AddSingleton(provider => new SeatJoinCoordinator(
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<ConnectionRegistry>(),
    provider.GetRequiredService<SeatBindingService>(),
    provider.GetRequiredService<AccountService>(),
    provider.GetRequiredService<AccountSessionRegistry>(),
    provider.GetRequiredService<ILogger<SeatJoinCoordinator>>()));
builder.Services.AddHostedService<GameBootstrapHostedService>();
builder.Services.AddHostedService<StepPacerHostedService>();
builder.Services.AddSignalR();

var app = builder.Build();
// 部署形态：前端构建产物随发布带上（见 csproj 的 wwwroot 接线），由宿主直接发页面，
// 因此页面与 /hub 同源——不需要 CORS，也不需要另起静态站点。开发期仍可继续用 web/ 的 Vite 服务器。
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet(
    "/healthz",
    () => Results.Ok(new
    {
        status = "ok",
        game = serverOptions.GameId,
        // 席位数量是服务端配置（说书人面板据此渲染席位）：前端启动时读它，避免与构建期变量分叉。
        seatCount = serverOptions.SeatCount,
    }));
app.MapHub<GameHub>("/hub/game");
app.MapHub<AccountHub>("/hub/account");
// SPA 回退：静态文件与既有端点都没匹配上、且路径不像文件时才交给前端路由（`/` 与 `/#player` 同一份构建）。
app.MapFallbackToFile("{*path:nonfile}", "index.html");
app.Run();

/// <summary>集成测试用的程序入口标记。</summary>
public partial class Program
{
}
