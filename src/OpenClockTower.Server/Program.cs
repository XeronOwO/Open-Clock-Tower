using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenClockTower.Application;
using OpenClockTower.Kernel;
using OpenClockTower.Rules;
using OpenClockTower.Server;

var builder = WebApplication.CreateBuilder(args);
var serverOptions = builder.Configuration
    .GetSection(GameServerOptions.SectionName)
    .Get<GameServerOptions>() ?? new GameServerOptions();
// 传输面上限与账号限速（M3 / G-A3-4 · G-A1-1）：取值显式、可配、在启动日志里可见（依赖 D-0031 / D-0032）。
var transportLimits = builder.Configuration
    .GetSection(TransportLimitsOptions.SectionName)
    .Get<TransportLimitsOptions>() ?? new TransportLimitsOptions();
var throttleOptions = builder.Configuration
    .GetSection(ThrottleOptions.SectionName)
    .Get<ThrottleOptions>() ?? new ThrottleOptions();
// 动作频率与桌数配额（M4 / G-A5-5 · G-A5-8）：同样显式取值、可配、启动可见（依赖 D-0033）。
var actionThrottleOptions = builder.Configuration
    .GetSection(ActionThrottleOptions.SectionName)
    .Get<ActionThrottleOptions>() ?? new ActionThrottleOptions();
var tableQuotaOptions = builder.Configuration
    .GetSection(TableQuotaOptions.SectionName)
    .Get<TableQuotaOptions>() ?? new TableQuotaOptions();

builder.Services.Configure<GameServerOptions>(
    builder.Configuration.GetSection(GameServerOptions.SectionName));
builder.Services.Configure<TransportLimitsOptions>(
    builder.Configuration.GetSection(TransportLimitsOptions.SectionName));
builder.Services.Configure<ThrottleOptions>(
    builder.Configuration.GetSection(ThrottleOptions.SectionName));
builder.Services.Configure<ActionThrottleOptions>(
    builder.Configuration.GetSection(ActionThrottleOptions.SectionName));
builder.Services.Configure<TableQuotaOptions>(
    builder.Configuration.GetSection(TableQuotaOptions.SectionName));

// 传输面上限（M3 / G-A3-4）：**显式取值**，不吃框架默认（30 MB 请求体 / 无上限连接 / 30 秒请求头超时）。
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = transportLimits.MaxRequestBodyBytes;
    kestrel.Limits.MaxConcurrentConnections = transportLimits.MaxConcurrentConnections;
    kestrel.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(transportLimits.RequestHeadersTimeoutSeconds);
    kestrel.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(transportLimits.KeepAliveTimeoutSeconds);
});

// HSTS（M3 / G-A3-2）：框架中间件只在 **HTTPS 响应**上加这个头，明文实例上发了也等于没发（浏览器按规范忽略）。
// 不 includeSubDomains、不 preload：别人的部署可能把本站挂在某个子域上，一条 HSTS 不该管到它的兄弟域。
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(30);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

// 反代真实 IP（M3 / G-A3-3）：只信回环 + 配置里的可信代理；名单写错在启动时抛，不静默降级。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    ForwardedHeaderPolicy.Apply(options, serverOptions.TrustedProxies));

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
// 自助注册开关（M4 / G-A5-2）：关掉之后没有任何开新账号的入口，策略对象在启动时打告警说明这一点。
builder.Services.AddSingleton(provider => new RegistrationPolicy(
    serverOptions,
    provider.GetRequiredService<ILogger<RegistrationPolicy>>()));
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
// 账号入口限速（M4 / G-A1-1）：进程内计数，重启即清零；键与阈值见 ThrottleOptions 与 D-0032。
builder.Services.AddSingleton<AccountAttemptLimiter>();
// 动作准入（M4 / G-A5-6 · G-A5-8）：入座与写文本的窗口额度；口径见 ActionThrottleOptions 与 D-0033。
builder.Services.AddSingleton<ActionThrottle>();
builder.Services.AddSingleton<HubActorResolver>();
builder.Services.AddSingleton<NotificationDispatcher>();
// 连接 ↔ 桌的绑定（多桌 D-0024）：单例——SignalR 的 Hub 每次调用新建实例，字段记不住东西。
// 桌标识只来自连接的 `?gameId=`（D-0027 删掉了"缺省回落默认桌"那条路）。
builder.Services.AddSingleton(provider => new HubGameScope(
    provider.GetRequiredService<GameRegistry>(),
    provider.GetRequiredService<NotificationDispatcher>(),
    provider.GetRequiredService<ActionThrottle>()));
// 加入入口（玩家与说书人两侧；自己解析所在桌，单例、无状态协作者）。
builder.Services.AddSingleton(provider => new HubJoinScope(
    provider.GetRequiredService<IGameCatalog>(),
    provider.GetRequiredService<HubGameScope>(),
    provider.GetRequiredService<SeatJoinCoordinator>(),
    provider.GetRequiredService<ConnectionRegistry>(),
    provider.GetRequiredService<AccountSessionRegistry>(),
    provider.GetRequiredService<NotificationDispatcher>(),
    provider.GetRequiredService<ActionThrottle>(),
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
// SignalR 的上限也**显式写出**（M3 / G-A3-4）：默认 32 KB 消息会随框架版本变，项目对此无感知。
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = transportLimits.MaxSignalRMessageBytes;
    options.MaximumParallelInvocationsPerClient = 1;
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.EnableDetailedErrors = false;
});

var app = builder.Build();

// 传输面接线顺序**就是安全性**：先按可信代理归一真实地址（后面所有日志、限速都依赖它），
// 再发响应头（安全头 + 缓存口径），然后按声明长度拦超限请求体，最后才轮到页面与端点。
app.UseForwardedHeaders();
app.UseHsts();
app.UseMiddleware<ResponseHeadersMiddleware>();
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

// 启动读数：这些值只活在配置里，出了问题首先要能一眼看到实际生效的是什么。
app.Logger.LogInformation(
    "传输面：请求体≤{MaxRequestBodyBytes}B · SignalR消息≤{MaxSignalRMessageBytes}B · 连接≤{MaxConnections} · "
    + "请求头超时={HeadersTimeout}s · 可信代理={TrustedProxies} · 登录限速={LoginFailuresPerUsername}次/{Window}s",
    transportLimits.MaxRequestBodyBytes,
    transportLimits.MaxSignalRMessageBytes,
    transportLimits.MaxConcurrentConnections,
    transportLimits.RequestHeadersTimeoutSeconds,
    serverOptions.TrustedProxies.Length == 0 ? "回环（默认）" : string.Join(",", serverOptions.TrustedProxies),
    throttleOptions.LoginFailuresPerUsername,
    throttleOptions.WindowSeconds);

// 滥用与风控的启动读数（M4 / D-0033）：配额与频率都只活在配置里，出事第一眼要能看到生效值是什么。
app.Logger.LogInformation(
    "风控面：自助注册={SelfRegistration} · 注册额度=每来源{RegisterPerClient}次/全局{RegisterGlobal}次每{Window}s · "
    + "在册桌上限=每账号{PerAccount}张/全局{Global}张 · 入座额度=每桌{JoinPerGame}次每{ActionWindow}s · 写文本额度=每身份{Actor}次每{ActionWindow}s",
    serverOptions.AllowSelfRegistration ? "开" : "关（没有任何开新账号的入口）",
    throttleOptions.RegisterCallsPerClient,
    throttleOptions.RegisterCallsGlobal,
    throttleOptions.WindowSeconds,
    tableQuotaOptions.MaxTablesPerAccount,
    tableQuotaOptions.MaxTablesGlobal,
    actionThrottleOptions.JoinCallsPerClientAndGame,
    actionThrottleOptions.WindowSeconds,
    actionThrottleOptions.WriteTextCallsPerActor,
    actionThrottleOptions.WindowSeconds);

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
