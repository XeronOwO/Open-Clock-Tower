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
    provider.GetRequiredService<ILogger<GameSession>>()));
// 复盘读侧（D-0020 / R-0043）：只读事件流 + 可见性闸，不依赖宿主内存态，因此独立于 GameSession 注册。
builder.Services.AddSingleton(provider => new ReplayQueryService(
    provider.GetRequiredService<GameId>(),
    provider.GetRequiredService<IGameStore>(),
    provider.GetRequiredService<ILogger<ReplayQueryService>>()));
builder.Services.AddSingleton<ConnectionRegistry>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddHostedService<GameBootstrapHostedService>();
builder.Services.AddHostedService<StepPacerHostedService>();
builder.Services.AddSignalR();

var app = builder.Build();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", game = serverOptions.GameId }));
app.MapHub<GameHub>("/hub/game");
app.Run();

/// <summary>集成测试用的程序入口标记。</summary>
public partial class Program
{
}
