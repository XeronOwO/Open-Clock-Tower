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
// 席位名读模型（D-0021）：姓名是会话信息，由启动装载 + 认领 / 改名 / 解除更新，视图投影只读快照。
builder.Services.AddSingleton<SeatNameDirectory>();
// 规则层的角色契约：提示目录与结算目录指向同一批实现（NightActions），常驻效果来源单列。
builder.Services.AddSingleton<IAbilityResolutionCatalog>(NightActions.Resolutions);
builder.Services.AddSingleton<IReadOnlyList<IStandingEffectSource>>(NightActions.StandingEffects);
builder.Services.AddSingleton(provider => new GameSession(
    provider.GetRequiredService<GameId>(),
    provider.GetRequiredService<IGameStore>(),
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<IAbilityResolutionCatalog>(),
    provider.GetRequiredService<IReadOnlyList<IStandingEffectSource>>(),
    provider.GetRequiredService<IClock>(),
    provider.GetRequiredService<PacingOptions>(),
    provider.GetRequiredService<SeatNameDirectory>(),
    provider.GetRequiredService<ILogger<GameSession>>()));
// 复盘读侧（D-0020 / R-0043）：只读事件流 + 可见性闸，不依赖宿主内存态，因此独立于 GameSession 注册。
builder.Services.AddSingleton(provider => new ReplayQueryService(
    provider.GetRequiredService<GameId>(),
    provider.GetRequiredService<IGameStore>(),
    provider.GetRequiredService<SeatNameDirectory>(),
    provider.GetRequiredService<ILogger<ReplayQueryService>>()));
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<HubActorResolver>();
builder.Services.AddSingleton<NotificationDispatcher>();
// 加入 / 认领的席位定位与凭据签发（D-0021）：从 GameHub 拆出（单文件 600 行门禁）。
builder.Services.AddSingleton(provider => new SeatJoinCoordinator(
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<GameId>(),
    provider.GetRequiredService<GameSession>(),
    provider.GetRequiredService<ConnectionRegistry>(),
    provider.GetRequiredService<SeatBindingService>(),
    provider.GetRequiredService<AccountService>(),
    provider.GetRequiredService<AccountSessionRegistry>(),
    provider.GetRequiredService<SeatNameDirectory>(),
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
