using System.Text.RegularExpressions;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 门禁：发给玩家的投影里**不得出现轮次 / 进度 / 他人活动信息，也不得出现说书人专属数据**（含房间健康位）。
/// </summary>
/// <remarks>
/// 依据 D-0013 §5（夜晚只有统一界面、不显示进度）与 D-0012 §4.3（信息隔离在服务端投影强制）：
/// 这里把"玩家能收到的字段名"本身锁死，防止将来有人顺手把内部状态塞进玩家投影。
/// 先红验证（2026-10-02）：给 `PlayerViewDto` 临时加 `Health` → 本门禁报红并指向该字段。
/// </remarks>
public sealed partial class PlayerProjectionLeakGateTests
{
    private static readonly string[] ScannedFiles =
    [
        Path.Combine("src", "OpenClockTower.Application", "PlayerView.cs"),
        Path.Combine("src", "OpenClockTower.Application", "PlayerEvent.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "PlayerViewDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "OperationRequestDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "ReconnectBundleDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "SeatJoinDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "PlayerEventDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "OperationRequestAnsweredDto.cs"),
        Path.Combine("src", "OpenClockTower.Contracts", "PhaseStartedDto.cs"),
    ];

    private static readonly string[] ForbiddenTokens =
    [
        // 轮次 / 进度（D-0013 §5）
        "Slot",
        "Step",
        "Progress",
        "Round",
        "Turn",
        "Remaining",

        // 状态账与效果归因（D-0012 §4.3）：它们是说书人视角的数据，不许出现在玩家投影里
        "Ledger",
        "Facts",
        "CausedBy",
        "Effects",
        "Termination",
        "Madness",
        "Seats",

        // 房间健康位（票据 room-health-degradation-flag）：恢复 / 重建失败的降级状态只说书人可见
        "Health",
        "Degraded",
    ];

    /// <summary>
    /// 玩家端源码里不得出现说书人专属的**字段名**（wire 词汇与专属模块）。
    /// </summary>
    /// <remarks>
    /// 服务端不把那些字段发给玩家，但如果玩家视图的代码去读它们，说明有人把"前端隐藏"当成了隔离
    /// （架构 §4.3 的反面教材：开发者工具一开就全泄了，D-0018）。
    /// 令牌用整词匹配：`seatCount` 不命中 `seat`，`SeatStateDto` 也不命中。
    /// </remarks>
    private static readonly string[] StorytellerOnlyTokens =
    [
        // 计划 / 进度（D-0013 §5）
        "slotIndex",
        "slotCount",
        "currentSlotId",
        "stepPlan",

        // 说书人视角的账与归因（D-0012 §4.3）
        "seats",
        "effects",
        "facts",
        "causedBy",
        "abilityUses",
        "malfunctions",
        "lastResolution",
        "terminationKind",
        "madness",

        // 说书人专属判定面与模块
        "awaitingDecision",
        "storyteller",
    ];

    /// <summary>玩家投影 / 玩家请求 DTO 里不得出现进度类字段与说书人专属字段（含房间健康位）。</summary>
    [Fact]
    public void PlayerFacingContracts_DeclareNoProgressOrOthersActivity()
    {
        var violations = new List<string>();
        foreach (var relativePath in ScannedFiles)
        {
            violations.AddRange(ForbiddenHits(
                relativePath,
                SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relativePath)))));
        }

        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            violations.Count == 0,
            "玩家投影里出现了不该下发的字段（进度类见 D-0013 §5；说书人专属 / 房间健康位见 D-0012 §4.3）。"
            + Environment.NewLine
            + report);
    }

    /// <summary>
    /// 名单自检：把"玩家投影里塞健康位"的样本喂给与正式门禁**同一条**判定，必须命中。
    /// 没有这条，黑名单一旦写错字，门禁会静默变绿——"先红"不能只靠一次性人工实验。
    /// </summary>
    [Fact]
    public void ForbiddenTokenScan_FlagsHealthAndDegradedSamples()
    {
        var hits = ForbiddenHits(
            Path.Combine("src", "OpenClockTower.Contracts", "PlayerViewDto.cs"),
            "public string? Health { get; init; } public bool Degraded { get; init; }");
        Assert.Contains(hits, hit => hit.EndsWith("→ Health", StringComparison.Ordinal));
        Assert.Contains(hits, hit => hit.EndsWith("→ Degraded", StringComparison.Ordinal));

        // 干净字段不误报：名单是整词子串匹配，普通字段不该被牵连。
        Assert.Empty(ForbiddenHits("sample.cs", "public required int Seat { get; init; }"));
    }

    /// <summary>扫描一段玩家投影源码，返回命中的禁词；正式门禁与名单自检共用这一条判定。</summary>
    private static List<string> ForbiddenHits(string relativePath, string code) =>
    [
        .. ForbiddenTokens
            .Where(token => code.Contains(token, StringComparison.Ordinal))
            .Select(token => $"{relativePath} → {token}"),
    ];

    /// <summary>玩家端 TypeScript / Vue 源码不得引用说书人专属字段或专属模块。</summary>
    [Fact]
    public void PlayerFeatureSources_DoNotReferenceStorytellerFields()
    {
        var playerFiles = new List<string>();
        foreach (var pattern in new[] { "*.ts", "*.vue" })
        {
            playerFiles.AddRange(RepositoryLayout.EnumerateFiles(pattern, "web", "src", "features", "player"));
            playerFiles.AddRange(RepositoryLayout.EnumerateFiles(pattern, "web", "src", "services"));
        }

        playerFiles = [.. playerFiles.Distinct().OrderBy(path => path, StringComparer.Ordinal)];
        Assert.NotEmpty(playerFiles);

        var sharedFiles = new List<string>();
        var violations = new List<string>();
        foreach (var relativePath in playerFiles)
        {
            if (Path.GetFileName(relativePath) == "storytellerGateway.ts"
                || Path.GetFileName(relativePath) == "storytellerCommands.ts")
            {
                continue;
            }

            sharedFiles.Add(relativePath);
            var code = SourceText.StripCommentsAndLiterals(File.ReadAllText(RepositoryLayout.PathOf(relativePath)));
            foreach (var token in StorytellerOnlyTokens)
            {
                if (WholeWord(token).IsMatch(code))
                {
                    violations.Add($"{relativePath} → {token}");
                }
            }
        }

        Assert.NotEmpty(sharedFiles);
        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            violations.Count == 0,
            "玩家端源码引用了说书人专属字段 / 模块（依据 D-0018 与架构 §4.3：隔离靠服务端不下发，不靠前端不显示）。"
            + Environment.NewLine
            + report);
    }

    /// <summary>
    /// 玩家端源码**只能依赖玩家侧模块**，且不得引用说书人专属的 DTO 类型。
    /// </summary>
    /// <remarks>
    /// 依据 D-0018 与架构 §4.3/§4.4。这条补上一个反例：共享层（`services/`、`display/`、`contracts/`）
    /// 才是玩家视图真正 import 的东西——只扫 `features/player` 自己的文本，字段从共享层"洗"一遍就绕过去了。
    /// 因此这里不扫文本内容，而是扫**依赖边**：玩家的每条 import 必须落在允许清单里，
    /// 并且不允许出现说书人专属类型名。
    /// </remarks>
    [Fact]
    public void PlayerSources_ImportOnlyPlayerSideModules()
    {
        var playerFiles = new List<string>();
        foreach (var pattern in new[] { "*.ts", "*.vue" })
        {
            playerFiles.AddRange(RepositoryLayout.EnumerateFiles(pattern, "web", "src", "features", "player"));
            playerFiles.AddRange(RepositoryLayout.EnumerateFiles(pattern, "web", "src", "services"));
        }

        playerFiles = [.. playerFiles.Distinct().OrderBy(path => path, StringComparer.Ordinal)];
        Assert.NotEmpty(playerFiles);

        var violations = new List<string>();
        var inspected = 0;
        foreach (var relativePath in playerFiles)
        {
            if (relativePath.EndsWith(".spec.ts", StringComparison.Ordinal))
            {
                continue;
            }

            // 说书人自己的连接与命令模块不是玩家侧模块（它们本来就在 services/ 下）。
            var fileName = Path.GetFileName(relativePath);
            if (fileName is "storytellerGateway.ts" or "storytellerCommands.ts")
            {
                continue;
            }

            inspected++;
            var code = StripCommentsAndLiterals(ReadFrontendSource(relativePath));
            foreach (Match import in ImportSourceText().Matches(code))
            {
                var module = import.Groups[1].Value;
                if (!PlayerSideModules.Contains(module))
                {
                    violations.Add($"{relativePath} → import {module}");
                }
            }

            foreach (var type in StorytellerOnlyTypes)
            {
                if (WholeWord(type).IsMatch(code))
                {
                    violations.Add($"{relativePath} → {type}");
                }
            }
        }

        Assert.True(
            inspected >= 5,
            $"只检查了 {inspected} 个玩家侧文件，扫描范围可能写错了"
            + Environment.NewLine
            + string.Join(Environment.NewLine, playerFiles));
        var report = string.Join(Environment.NewLine, violations);
        Assert.True(
            violations.Count == 0,
            "玩家端依赖了说书人侧模块 / 类型（依据 D-0018 与架构 §4.3：玩家视图不许经由共享层拿到说书人数据）。"
            + Environment.NewLine
            + report);
    }

    /// <summary>玩家侧允许依赖的模块清单（故意写死：新增依赖必须显式加进来并被复核）。</summary>
    private static readonly HashSet<string> PlayerSideModules = new(StringComparer.Ordinal)
    {
        "vue",
        "@microsoft/signalr",
        "@/contracts/game",
        "@/display/format",
        "@/display/labels",
        "@/services/connectionState",
        "@/services/idempotency",
        "@/services/playerGateway",
        "@/services/playerViewMerge",
        "@/services/ticketStore",
        // 玩家侧自己的子组件（白天操作区）；新增依赖必须显式登记并复核（见上方注释）。
        "@/features/player/PlayerDayPanel.vue",
    };

    /// <summary>说书人专属的 DTO 类型名；玩家侧出现任何一个都说明越界。</summary>
    private static readonly string[] StorytellerOnlyTypes =
    [
        "StorytellerViewDto",
        "RoomHealthDto",
        "SeatStateDto",
        "SeatStateFactDto",
        "EffectDto",
        "AbilityUseDto",
        "MalfunctionDto",
        "AbilityResolutionDto",
        "SeatChangeDto",
        "PendingRequestDto",
        "normalizeStorytellerView",
    ];

    private static Regex WholeWord(string token) =>
        new($@"(?<![A-Za-z0-9_]){Regex.Escape(token)}(?![A-Za-z0-9_])");

    /// <summary>
    /// 读取前端源码用于扫描：`.vue` 只取 `<script setup>` 块——模板与样式里成对的 `{ }`
    /// 会被 <see cref="SourceText"/> 当成块注释吞掉，连整个脚本块一起吃掉（实测踩过：门禁因此变成假绿）。
    /// </summary>
    private static string ReadFrontendSource(string relativePath)
    {
        var text = File.ReadAllText(RepositoryLayout.PathOf(relativePath));
        if (!relativePath.EndsWith(".vue", StringComparison.Ordinal))
        {
            return text;
        }

        var match = VueScriptBlock().Match(text);
        Assert.True(match.Success, $"{relativePath}：找不到 <script setup> 块，前端扫描会变成空转");
        return match.Groups[1].Value;
    }

    /// <summary>前端源码清洗：只去块注释——`//` 之后的字面量在 TS 里是合法字符串内容，不能按 C# 规则切。</summary>
    private static string StripCommentsAndLiterals(string source) =>
        BlockComment().Replace(source, " ");

    /// <summary>匹配 `from '...'` / `import '...'` 里的模块路径。</summary>
    [GeneratedRegex(@"(?:from|import)\s*'([^']+)'")]
    private static partial Regex ImportSourceText();

    [GeneratedRegex(@"<script[^>]*>(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex VueScriptBlock();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
