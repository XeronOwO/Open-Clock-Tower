using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using OpenClockTower.Contracts;
using OpenClockTower.Kernel;
using OpenClockTower.Server;

namespace OpenClockTower.Integration.Tests;

/// <summary>
/// 授权面（M2 / G-A4-6）：把 <c>docs/security/authorization-matrix.md</c> 的「方法 × 身份」矩阵
/// 写成**可执行的表**，再用三种身份各扫一遍 GameHub 的每一个公开方法。
/// </summary>
/// <remarks>
/// <para>
/// 为什么要有这一页：矩阵里 23 个说书人命令此前只有正面用例，"玩家调它会被拒"这件事
/// 只是代码今天恰好写对了——**改坏不会有任何人发现**。本页把它变成会红的用例。
/// </para>
/// <para>
/// 断言只落在**身份这一维**：每个方法在每种身份下，要么必须被身份闸拒（给出期望的拒绝标识），
/// 要么必须**不**被身份闸拒（它之后可能被阶段 / 合法性闸拒，那不是本页要看的）。因此每个方法
/// 都不需要构造出"能成功"的局面，表也不会随玩法改动而腐烂；反过来说，本页**不**证明命令能成功，
/// 那是各角色自己的验收矩阵（<c>docs/acceptance/AGENTS.md</c> §4）的事。
/// </para>
/// <para>
/// 表与文档同源：<see cref="Matrix"/> 一行对应矩阵的一行，注释里带 <c>★</c> 的 23 行就是 G-A4-6
/// 的原始缺口。新增 Hub 方法必须在这里表态——<see cref="Matrix_CoversEveryPublicGameHubMethod"/>
/// 会在漏行时变红。AccountHub 的身份面（大厅 / 账号入口）不在本页，由
/// <c>AccountHostTests</c> / <c>LobbyHostTests</c> / <c>SessionRevocationHostTests</c> 覆盖。
/// </para>
/// </remarks>
public sealed class AuthorizationSurfaceHostTests
{
    /// <summary>Hub 层"只认说书人连接"的拒绝标识（查询类入口不走四道闸，抛 <see cref="HubException"/>）。</summary>
    private const string HubStorytellerGate = "hub.storyteller_only";

    /// <summary>该拒绝的文案片段（与 <c>HubActorResolver.ResolveStoryteller</c> 一致）。</summary>
    private const string HubStorytellerGateText = "不是有效的说书人连接";

    /// <summary>凭据闸的拒绝标识与文案片段（与 <c>HubActorResolver.Resolve</c> 一致）。</summary>
    private const string CredentialGate = "hub.credential";

    private const string CredentialGateText = "连接凭据无效";

    /// <summary>返回值不是 <c>Rejected</c> 时的前缀（表示这条命令过了身份闸）。</summary>
    private const string AcceptedPrefix = "ok:";

    /// <summary>
    /// 授权矩阵（依据 <c>docs/security/authorization-matrix.md</c>，2026-10-06 M2 第二刀核对）。
    /// </summary>
    /// <remarks>
    /// 参数一律取"**在合法性上必然不成立**"的值（不存在的席位、不存在的请求 / 裁定点、不存在的注记）：
    /// 这样被允许的身份会停在阶段 / 合法性闸上——既证明它过了身份闸，又不会真的改动夹具状态。
    /// </remarks>
    private static readonly MatrixRow[] Matrix =
    [
        // —— 凭据签发路径：这四个方法不收连接凭据，"谁是谁"由邀请码与账号会话决定，不在本表驱动 ——
        NotDriven("JoinByInviteCode", "凭邀请码入座（必须登录，D-0037）：AccountHostTests.Join_WithInvalidAccountSession_IsRejected + SelfServiceJoinHostTests.InviteOnlyTable_RejectsSelfService_ButLetsInviteCodeHolderIn"),
        NotDriven("JoinTable", "自助入座（必须登录 + 公开桌 + 未开局）：SelfServiceJoinHostTests 八条正反用例"),
        NotDriven("JoinStorytellerWithAccount", "主持台认开桌账号：MultiTableHostIsolationTests / LobbyHostTests"),

        // —— 玩家专属命令：说书人调必须被身份闸拒（反向），玩家调只要求"过了身份闸" ——
        PlayerOnly("SubmitResponse", ["req-not-current", "seat:2", "g-a4-6-submit-response", 0L]),
        PlayerOnly("AskArtistQuestion", ["授权面用例：只有玩家本人可以向说书人提问", "g-a4-6-artist-question"]),
        PlayerOnly("AskSavantQuestion", ["g-a4-6-savant-question"]),
        PlayerOnly("MakeJugglerGuesses", [null, "g-a4-6-juggler-guesses"]),
        PlayerOnly("Nominate", [2, "g-a4-6-nominate"], "提名者由凭据推导，命令面无法自称身份"),
        PlayerOnly("NominateExtra", [2, "g-a4-6-nominate-extra"], "★ 屠夫的额外提名窗口"),
        PlayerOnly("CastVote", [1, true, "g-a4-6-cast-vote"]),
        PlayerOnly("ProposeExile", [2, "g-a4-6-propose-exile"], "★ 流放提议（含死者，R-0044）"),
        PlayerOnly("CastExileVote", [1, true, "g-a4-6-cast-exile-vote"], "★ 流放表决举手"),
        PlayerOnly("RequestTravellerDeparture", [null, "g-a4-6-request-departure"], "★ 离场申请：席位由凭据推导，玩家只能替自己申请"),

        // —— 说书人专属命令：玩家调必须被身份闸拒 ——
        StorytellerOnly("VoidRequest", ["req-not-current", "StorytellerForce", null, "g-a4-6-void-request"], "★ 强制作废"),
        StorytellerOnly("ProxyFill", ["req-not-current", "seat:2", null, "g-a4-6-proxy-fill"], "★ 代填"),
        StorytellerOnly("ForceAdvance", ["授权面用例：只有说书人可以强推", "g-a4-6-force-advance"]),
        StorytellerOnly("TakeOver", ["授权面用例：只有说书人可以接管", "g-a4-6-take-over"], "★ 接管"),
        StorytellerOnly("ReleaseControl", ["授权面用例：只有说书人可以交还自动化", "g-a4-6-release-control"], "★ 交还"),
        StorytellerOnly("ResolveDecisionPoint", ["point-not-current", null, null, "g-a4-6-decision-point"], "★ 了结裁定点"),
        StorytellerOnly(
            "ReportSeatState",
            [99, "Alive", null, null, null, null, "授权面用例：只有说书人可以上报座位状态", null, "g-a4-6-report-seat-state"],
            "★ 上帝视角的状态上报：能改任何一席的角色 / 阵营 / 生死"),
        HostOnly("AssignCharacters", [Array.Empty<SeatCharacterAssignmentDto>(), "g-a4-6-assign-characters"], "★ 开局分配"),
        StorytellerOnly("JoinTraveller", [99, "barista", "Good", null, "g-a4-6-join-traveller"]),
        StorytellerOnly("RemoveTraveller", [99, null, "g-a4-6-remove-traveller"]),
        StorytellerOnly("ResolveTravellerDeparture", [99, true, null, "g-a4-6-resolve-departure"], "★ 裁定离场申请：批准即执行座位离场"),
        HostOnly("StartNight", [99, "Original", "g-a4-6-start-night"], "★ 开夜"),
        HostOnly("StartDay", ["g-a4-6-start-day"]),
        StorytellerOnly("StartVoteSweep", [1, 3000, 1000, "g-a4-6-start-vote-sweep"], "★ 开始收票"),
        StorytellerOnly("ResumeVoteSweep", [1, "g-a4-6-resume-vote-sweep"], "★ 继续收票"),
        StorytellerOnly("CountVotes", [1, "g-a4-6-count-votes"], "★ 计票：直接决定生死"),
        StorytellerOnly("CloseDay", ["g-a4-6-close-day"], "★ 结束白天：处决当前待处决者"),
        StorytellerOnly("StartExileSweep", [1, 3000, 1000, "g-a4-6-start-exile-sweep"], "★ 开始流放收票"),
        StorytellerOnly("ResumeExileSweep", [1, "g-a4-6-resume-exile-sweep"], "★ 继续流放收票"),
        StorytellerOnly("CountExileVotes", [1, "g-a4-6-count-exile-votes"], "★ 流放计票"),
        StorytellerOnly("ResolveDayProtection", [99, true, null, "g-a4-6-day-protection"], "★ 裁定当天的死亡保护"),
        StorytellerOnly("PunishExecution", [99, "Mutant", null, "g-a4-6-punish-execution"], "★ 处罚处决：直接杀人"),
        StorytellerOnly("PitHagCasualty", [99, null, "g-a4-6-pit-hag-casualty"], "★ 麻脸巫婆之夜追加死亡：直接杀人"),
        StorytellerOnly("ResolveDeferredDeath", [99, true, null, "g-a4-6-deferred-death"], "★ 裁定待定死亡"),
        StorytellerOnly("RebuildRoom", ["授权面用例：只有说书人可以重建房间", "g-a4-6-rebuild-room"], "★ 重建会改写派生状态"),
        StorytellerOnly("AddSeatAnnotation", [99, "授权面用例", "g-a4-6-add-annotation"]),
        StorytellerOnly("UpdateSeatAnnotation", [999, "授权面用例", "g-a4-6-update-annotation"]),
        StorytellerOnly("RemoveSeatAnnotation", [999, "g-a4-6-remove-annotation"]),

        // —— 桌务与查询：不走四道闸，Hub 层直接要求说书人身份（玩家被拒时抛 HubException）——
        StorytellerQuery("ReleaseSeatBinding", [99], "无界面入口，见 G-A4-7"),
        StorytellerQuery("SetTableInviteOnly", [true], "访问模式开关（D-0037）：切换后推给该桌全部连接"),
        StorytellerQuery("ProposeSetup", [null, null]),
        StorytellerQuery("GetStorytellerView", []),

        // —— 两类身份都允许：只要求"没被身份闸拦"（防的是过度拦截，不是越权）——
        AnyIdentity("GetReplay", [0L, 10], "说书人随时可读；玩家的可见性闸在 Application（R-0043），不是身份闸"),
    ];

    /// <summary>玩家席位连接扫一遍矩阵：说书人命令必须被拒，玩家命令不得被拒。</summary>
    [Fact]
    public async Task PlayerConnection_MatchesAuthorizationMatrix()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await using var player = await host.ConnectSeatAsync(new SeatId(1));

        await RunSweepAsync(MatrixIdentity.Player, row => InvokeAsync(player, row));
    }

    /// <summary>说书人连接扫一遍矩阵：玩家命令必须被拒，说书人命令不得被拒。</summary>
    [Fact]
    public async Task StorytellerConnection_MatchesAuthorizationMatrix()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await using var storyteller = await host.ConnectStorytellerAsync();

        await RunSweepAsync(MatrixIdentity.Storyteller, row => InvokeAsync(storyteller, row));
    }

    /// <summary>
    /// 没加入过的匿名连接出示伪造凭据：**每个**收凭据的方法都必须在前门被拒——
    /// 审计第 1 步只抽样了 9 个方法，这里是全量。
    /// </summary>
    [Fact]
    public async Task AnonymousConnection_IsRejectedOnEveryCredentialedMethod()
    {
        await using var host = new TestServerHost(seatCount: 3);
        await using var connection = await host.ConnectAnonymousAsync();
        var forged = new GameClient(connection, "伪造凭据");

        await RunSweepAsync(MatrixIdentity.Anonymous, row => InvokeAsync(forged, row));
    }

    /// <summary>矩阵必须覆盖 GameHub 的每一个公开方法：新增方法不表态就红。</summary>
    [Fact]
    public void Matrix_CoversEveryPublicGameHubMethod()
    {
        var declared = typeof(GameHub)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .Where(name => !string.Equals(name, nameof(GameHub.OnDisconnectedAsync), StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var covered = Matrix.Select(row => row.Method).ToHashSet(StringComparer.Ordinal);
        var missing = declared.Where(name => !covered.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var unknown = covered.Where(name => !declared.Contains(name, StringComparer.Ordinal)).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var silentExemptions = Matrix
            .Where(row => row.DeniedFor == DeniedFor.NotDriven && string.IsNullOrWhiteSpace(row.Note))
            .Select(row => row.Method)
            .ToList();

        Assert.True(
            missing.Count == 0 && unknown.Count == 0 && silentExemptions.Count == 0,
            "矩阵与 GameHub 的公开方法不一致（依据 docs/security/authorization-matrix.md）。" + Environment.NewLine
            + "新增一个 Hub 方法就要在这里表态：谁不能调（StorytellerOnly / HostOnly / PlayerOnly / StorytellerQuery），"
            + "或写明为什么不由本表驱动（NotDriven 必须给出指名的既有用例）。" + Environment.NewLine
            + $"GameHub 有、表里没有：{string.Join("、", missing)}" + Environment.NewLine
            + $"表里有、GameHub 没有：{string.Join("、", unknown)}" + Environment.NewLine
            + $"NotDriven 但没写由谁覆盖：{string.Join("、", silentExemptions)}");
    }

    /// <summary>逐行核对：这一行的期望与观测是否一致；不一致的行全部收集起来一次报出。</summary>
    private static async Task RunSweepAsync(MatrixIdentity identity, Func<MatrixRow, Task<string>> invoke)
    {
        var mismatches = new List<string>();
        var driven = 0;
        foreach (var row in Matrix)
        {
            if (row.DeniedFor == DeniedFor.NotDriven)
            {
                continue;
            }

            driven++;
            var actual = await invoke(row);
            var expected = Expected(identity, row);
            if (expected is null)
            {
                if (IsIdentityRejection(actual))
                {
                    mismatches.Add($"{row.Method}：{Label(identity)}不该被身份闸拒，实际「{Describe(actual)}」");
                }

                continue;
            }

            if (!Matches(actual, expected))
            {
                mismatches.Add($"{row.Method}：{Label(identity)}期望「{Describe(expected)}」，实际「{Describe(actual)}」{Hint(actual)}");
            }
        }

        Assert.True(
            mismatches.Count == 0,
            $"授权面与矩阵不一致（{Label(identity)}）：{mismatches.Count} / {driven} 行有出入。" + Environment.NewLine
            + "只有两条正当改法：把服务端的身份闸修回去，或改本表并同步 docs/security/authorization-matrix.md。"
            + Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    /// <summary>这条身份在这一行上必须被拒吗：null = 不得被身份闸拒。</summary>
    private static string? Expected(MatrixIdentity identity, MatrixRow row) => identity switch
    {
        MatrixIdentity.Player when row.DeniedFor == DeniedFor.Players => row.DeniedAs,
        MatrixIdentity.Storyteller when row.DeniedFor == DeniedFor.Storytellers => row.DeniedAs,
        MatrixIdentity.Anonymous => CredentialGate,
        _ => null,
    };

    /// <summary>按凭据自动出示的方式调一次；把"被拒 / 过了身份闸"归一成一个可比较的标识。</summary>
    private static async Task<string> InvokeAsync(GameClient client, MatrixRow row)
    {
        try
        {
            if (row.Style == CallStyle.Command)
            {
                return Classify(await client.InvokeAsync<CommandResultDto>(row.Method, row.Arguments));
            }

            await client.InvokeAsync<object?>(row.Method, row.Arguments);
            return AcceptedPrefix + "query";
        }
        catch (HubException exception)
        {
            return $"hub:{exception.Message}";
        }
    }

    private static string Classify(CommandResultDto result) =>
        string.Equals(result.Kind, "Rejected", StringComparison.Ordinal) && result.RejectionCode is { } code
            ? code
            : AcceptedPrefix + result.Kind;

    /// <summary>被拒的标识算不算"身份闸拒"（含 Hub 层只认说书人连接的那条）。</summary>
    private static bool IsIdentityRejection(string actual) =>
        actual.StartsWith("identity.", StringComparison.Ordinal)
        || actual.Contains(HubStorytellerGateText, StringComparison.Ordinal);

    private static bool Matches(string actual, string expected) => expected switch
    {
        HubStorytellerGate => actual.StartsWith("hub:", StringComparison.Ordinal)
            && actual.Contains(HubStorytellerGateText, StringComparison.Ordinal),
        CredentialGate => actual.StartsWith("hub:", StringComparison.Ordinal)
            && actual.Contains(CredentialGateText, StringComparison.Ordinal),
        _ => string.Equals(actual, expected, StringComparison.Ordinal),
    };

    private static string Describe(string marker) => marker switch
    {
        HubStorytellerGate => "Hub 层说书人闸拒（不是有效的说书人连接）",
        CredentialGate => "凭据闸拒（连接凭据无效）",
        _ when marker.StartsWith(AcceptedPrefix, StringComparison.Ordinal)
            => $"没有被身份闸拒（{marker[AcceptedPrefix.Length..]}）",
        _ => marker,
    };

    private static string Hint(string actual) =>
        actual.StartsWith(AcceptedPrefix, StringComparison.Ordinal) ? " —— 身份闸没拦住这条命令" : string.Empty;

    private static string Label(MatrixIdentity identity) => identity switch
    {
        MatrixIdentity.Player => "玩家席位连接",
        MatrixIdentity.Storyteller => "说书人连接",
        _ => "匿名连接",
    };

    private static MatrixRow NotDriven(string method, string note) =>
        new(method, [], CallStyle.Command, DeniedFor.NotDriven, string.Empty, note);

    private static MatrixRow StorytellerOnly(string method, object?[] arguments, string note = "") =>
        new(method, arguments, CallStyle.Command, DeniedFor.Players, "identity.storyteller_only", note);

    private static MatrixRow HostOnly(string method, object?[] arguments, string note = "") =>
        new(method, arguments, CallStyle.Command, DeniedFor.Players, "identity.host_only", note);

    private static MatrixRow PlayerOnly(string method, object?[] arguments, string note = "") =>
        new(method, arguments, CallStyle.Command, DeniedFor.Storytellers, "identity.player_only", note);

    private static MatrixRow StorytellerQuery(string method, object?[] arguments, string note = "") =>
        new(method, arguments, CallStyle.Query, DeniedFor.Players, HubStorytellerGate, note);

    private static MatrixRow AnyIdentity(string method, object?[] arguments, string note = "") =>
        new(method, arguments, CallStyle.Query, DeniedFor.Nobody, string.Empty, note);

    /// <summary>被扫的身份：一条玩家席位连接 / 一条说书人连接 / 一条没加入过的匿名连接。</summary>
    private enum MatrixIdentity
    {
        Player,
        Storyteller,
        Anonymous,
    }

    /// <summary>调用形态：命令走四道闸并回 <see cref="CommandResultDto"/>；查询在 Hub 层就判身份。</summary>
    private enum CallStyle
    {
        Command,
        Query,
    }

    /// <summary>哪一类身份**不允许**调这个方法。</summary>
    private enum DeniedFor
    {
        Players,
        Storytellers,
        Nobody,
        NotDriven,
    }

    /// <summary>矩阵的一行：方法 + 调用参数 + 谁不能调 + 期望的拒绝标识。</summary>
    private sealed record MatrixRow(
        string Method,
        object?[] Arguments,
        CallStyle Style,
        DeniedFor DeniedFor,
        string DeniedAs,
        string Note);
}
