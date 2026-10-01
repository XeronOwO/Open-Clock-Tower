using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;
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
builder.Services.AddSingleton(provider => new GameSession(
    provider.GetRequiredService<GameId>(),
    provider.GetRequiredService<IGameStore>(),
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<IClock>(),
    provider.GetRequiredService<PacingOptions>(),
    provider.GetRequiredService<ILogger<GameSession>>()));
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
