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
// 运维身份名单（D-0026）：来自本机配置的登录名清单；清单为空 = 谁都不是运维身份。
// 它不是"说书人"——说书人由该桌票据认定，任何登录账号开一桌就得到它。
builder.Services.AddSingleton(provider => new AdminDirectory(builder.Configuration));
// 开桌授权（D-0026）：默认任何登录账号都能开；配 GameServer:AllowPlayerTables=false 收口为只有运维能开。
// "收口 + 没配运维名单 = 谁都开不了桌"这条死路由策略对象在启动时打告警，不静默。
builder.Services.AddSingleton(provider => new TableCreationPolicy(
    serverOptions,
    provider.GetRequiredService<AdminDirectory>(),
    provider.GetRequiredService<ILogger<TableCreationPolicy>>()));
// 大厅用例（D-0025）：列桌 / 开桌。
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
    provider.GetRequiredService<ILoggerFactory>()));
builder.Services.AddSingleton<ConnectionRegistry>();
// 撤销编排（M2 / G-A2-1）：撤账号会话与撤"由它授权的在线连接"必须同批——登出 / 口令重置只走它。
builder.Services.AddSingleton<AccountRevocationService>();
builder.Services.AddSingleton<HubActorResolver>();
builder.Services.AddSingleton<NotificationDispatcher>();
// 连接 ↔ 桌的绑定（多桌 D-0024）：单例——SignalR 的 Hub 每次调用新建实例，字段记不住东西。
// 桌标识只来自连接的 `?gameId=`（D-0027 删掉了"缺省回落默认桌"那条路）。
builder.Services.AddSingleton(provider => new HubGameScope(
    provider.GetRequiredService<GameRegistry>(),
    provider.GetRequiredService<NotificationDispatcher>()));
// 加入入口（玩家与说书人两侧；自己解析所在桌，单例、无状态协作者）。
builder.Services.AddSingleton(provider => new HubJoinScope(
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<HubGameScope>(),
    provider.GetRequiredService<SeatJoinCoordinator>(),
    provider.GetRequiredService<ConnectionRegistry>(),
    provider.GetRequiredService<AccountSessionRegistry>(),
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
        // 席位数量是服务端配置（开桌表单预填与面板渲染兜底）：前端启动时读它，避免与构建期变量分叉。
        // 这里**不再有 `game` 字段**：默认桌已随 D-0027 退场，宿主没有"自己那一桌"了。
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
