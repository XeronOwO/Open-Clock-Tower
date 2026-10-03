using System.Security.Cryptography;
using System.Text;
using OpenClockTower.Kernel;

namespace OpenClockTower.Rules;

/// <summary>
/// 配板求解：按分布表的基线 + 在场角色的设置调整算出净分布，再用**显式种子**确定性地抽取角色袋。
/// </summary>
/// <remarks>
/// <para>
/// 口径见 <c>docs/standard/rulings.md</c> R-0041（分布表来源与逐行取证等级）与 R-0042
/// （同类修正先加总、再按剧本池与人数约束钳制、缺额由镇民补偿；「超池」按官方口径钳制并**显式披露**；
/// 随机只作显式输入）。配板是**建议**：不落账、不进事件流——提交仍走既有分配命令面（D-0017），
/// 重放只折显式分配、不重摇（D-0008 / D-0011）。
/// </para>
/// <para>
/// 求解是「按当前已选集合重算目标 → 补齐 / 撤下 → 再看是否自洽」的有界迭代：同一个种子得到同一个
/// 结果，报错也报得出来（不静默给一个说不清的配板）。S&amp;V 的两条修正都在恶魔身上，一两轮即收敛；
/// 合成剧本若出现互相抵消的修正，会在 <see cref="MaxRounds"/> 轮后显式失败（R-0042 第 3 条）。
/// </para>
/// </remarks>
public static class SetupComposer
{
    /// <summary>自洽迭代轮数上限：超过即显式失败（R-0042 第 3 条）。</summary>
    private const int MaxRounds = 16;

    /// <summary>补齐 / 撤下的固定类型顺序——顺序固定，同一种子才有同一个结果。</summary>
    private static readonly CharacterType[] PaddingOrder =
    [
        CharacterType.Demon,
        CharacterType.Minion,
        CharacterType.Outsider,
        CharacterType.Townsfolk,
    ];

    /// <summary>用《梦殒春宵》花名册配板：人数 + 显式种子 → 角色袋或显式失败。</summary>
    public static SetupComposeResult Compose(int playerCount, string seed) =>
        Compose(SectsAndVioletsRoster.AsSetupScript(), playerCount, seed);

    /// <summary>用给定剧本数据配板（合成剧本用于边界用例；种子必须显式给出）。</summary>
    public static SetupComposeResult Compose(SetupScript script, int playerCount, string seed)
    {
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);

        if (SectsAndVioletsDistribution.BaseFor(playerCount) is not { } baseline)
        {
            return SetupComposeResult.Failed(
                SetupComposeResult.FailureCode.PlayerCountUnsupported,
                $"人数 {playerCount} 不在设置分布表的范围内（5–15 人，R-0041）。");
        }

        var random = new SeededRandom(SeedState(seed));
        var selected = PaddingOrder.ToDictionary(type => type, _ => new List<SetupPoolEntry>());

        for (var round = 1; round <= MaxRounds; round++)
        {
            if (TryTargets(baseline, selected, playerCount, script, out var targets, out var notes)
                is { } targetFailure)
            {
                return targetFailure;
            }

            if (SelectedCounts(selected) == targets)
            {
                return SetupComposeResult.Success(new SetupProposal(
                    [.. PaddingOrder.SelectMany(type => selected[type]).Select(entry => entry.Character)],
                    targets,
                    notes));
            }

            if (TryPad(script, selected, targets, random) is { } padFailure)
            {
                return padFailure;
            }
        }

        return SetupComposeResult.Failed(
            SetupComposeResult.FailureCode.DistributionConflict,
            $"设置调整在 {MaxRounds} 轮内没有收敛：出场角色的修正互相冲突（R-0042 第 3 条）。");
    }

    /// <summary>
    /// 按当前已选集合算目标净分布：同类修正先加总 → 逐类型钳制 → 镇民取余量；
    /// 同时生成对说书人可见的说明（修正来源与钳制事实）。返回 null = 成功。
    /// </summary>
    private static SetupComposeResult? TryTargets(
        SetupCounts baseline,
        Dictionary<CharacterType, List<SetupPoolEntry>> selected,
        int playerCount,
        SetupScript script,
        out SetupCounts targets,
        out IReadOnlyList<string> notes)
    {
        targets = baseline;

        // 基线本身撑不起剧本（与修正无关的结构性缺池）→ 显式失败，不许被钳制悄悄盖过去。
        foreach (var type in PaddingOrder)
        {
            if (baseline.Of(type) > script.PoolOf(type).Count)
            {
                notes = [];
                return SetupComposeResult.Failed(
                    SetupComposeResult.FailureCode.PoolExhausted,
                    $"剧本{TypeLabel(type)}只有 {script.PoolOf(type).Count} 名，配不出基线的 {baseline.Of(type)} 名（R-0042 第 3 条）。");
            }
        }

        var deltas = new Dictionary<CharacterType, int>();
        foreach (var entry in PaddingOrder.SelectMany(type => selected[type]))
        {
            foreach (var adjustment in entry.Adjustments)
            {
                deltas[adjustment.Type] = deltas.GetValueOrDefault(adjustment.Type) + adjustment.Delta;
            }
        }

        // 镇民上的数量修正（提线木偶一类）本版不支持：不静默忽略，直接显式失败（R-0042 第 4 条）。
        if (deltas.GetValueOrDefault(CharacterType.Townsfolk) != 0)
        {
            notes = [];
            return SetupComposeResult.Failed(
                SetupComposeResult.FailureCode.DistributionConflict,
                "本版不支持以镇民为目标的数量修正（R-0042 第 4 条：范围型与特殊设置方式不在首版范围）。");
        }

        var demons = Clamp(
            baseline.Demons + deltas.GetValueOrDefault(CharacterType.Demon),
            0,
            Math.Min(script.Demons.Count, playerCount));
        var minions = Clamp(
            baseline.Minions + deltas.GetValueOrDefault(CharacterType.Minion),
            0,
            Math.Min(script.Minions.Count, playerCount - demons));
        var outsiders = Clamp(
            baseline.Outsiders + deltas.GetValueOrDefault(CharacterType.Outsider),
            0,
            Math.Min(script.Outsiders.Count, playerCount - demons - minions));
        var townsfolk = playerCount - demons - minions - outsiders;

        if (townsfolk < 0)
        {
            notes = [];
            return SetupComposeResult.Failed(
                SetupComposeResult.FailureCode.DistributionConflict,
                $"净分布配不出：恶魔 {demons} + 爪牙 {minions} + 外来者 {outsiders} 超过玩家人数 {playerCount}（R-0042 第 3 条）。");
        }

        if (townsfolk > script.Townsfolk.Count)
        {
            notes = [];
            return SetupComposeResult.Failed(
                SetupComposeResult.FailureCode.PoolExhausted,
                $"剧本镇民只有 {script.Townsfolk.Count} 名，配不出 {townsfolk} 名（R-0042 第 3 条）。");
        }

        targets = new SetupCounts(townsfolk, outsiders, minions, demons);
        notes = BuildNotes(selected, baseline, townsfolk, deltas, demons, minions, outsiders);
        return null;
    }

    /// <summary>把已选集合补齐 / 撤下到目标计数；返回 null = 成功。</summary>
    private static SetupComposeResult? TryPad(
        SetupScript script,
        Dictionary<CharacterType, List<SetupPoolEntry>> selected,
        SetupCounts targets,
        SeededRandom random)
    {
        foreach (var type in PaddingOrder)
        {
            var list = selected[type];
            var need = targets.Of(type) - list.Count;

            if (need < 0)
            {
                var remove = -need;
                if (list.Count < remove)
                {
                    return SetupComposeResult.Failed(
                        SetupComposeResult.FailureCode.DistributionConflict,
                        $"净分布要求撤下 {remove} 名{TypeLabel(type)}，但当前只选了 {list.Count} 名：修正互相冲突（R-0042 第 3 条）。");
                }

                list.RemoveRange(list.Count - remove, remove);
                continue;
            }

            if (need == 0)
            {
                continue;
            }

            var taken = PaddingOrder
                .SelectMany(item => selected[item])
                .Select(entry => entry.Character)
                .ToHashSet();
            var available = script.PoolOf(type).Where(entry => !taken.Contains(entry.Character)).ToList();
            if (available.Count < need)
            {
                return SetupComposeResult.Failed(
                    SetupComposeResult.FailureCode.PoolExhausted,
                    $"剧本{TypeLabel(type)}只剩 {available.Count} 名可选，配不出 {need} 名（R-0042 第 3 条）。");
            }

            Shuffle(available, random);
            list.AddRange(available.Take(need));
        }

        return null;
    }

    /// <summary>对说书人可见的说明：谁带来了什么修正、哪一类被钳制到了多少（R-0042 第 2 条）。</summary>
    private static List<string> BuildNotes(
        Dictionary<CharacterType, List<SetupPoolEntry>> selected,
        SetupCounts baseline,
        int townsfolk,
        IReadOnlyDictionary<CharacterType, int> deltas,
        int demons,
        int minions,
        int outsiders)
    {
        var notes = new List<string>();

        foreach (var entry in PaddingOrder.SelectMany(type => selected[type]))
        {
            if (entry.Adjustments.Count == 0)
            {
                continue;
            }

            var described = string.Join(
                "、",
                entry.Adjustments.Select(adjustment =>
                    $"{TypeLabel(adjustment.Type)} {(adjustment.Delta > 0 ? "+" : string.Empty)}{adjustment.Delta}"));
            notes.Add($"设置调整 · {SectsAndVioletsRoster.DisplayNameOf(entry.Character) ?? entry.Character.Value}：{described}（缺额由镇民补偿）");
        }

        AddClampNote(notes, "恶魔", baseline.Demons, deltas.GetValueOrDefault(CharacterType.Demon), demons);
        AddClampNote(notes, "爪牙", baseline.Minions, deltas.GetValueOrDefault(CharacterType.Minion), minions);
        AddClampNote(notes, "外来者", baseline.Outsiders, deltas.GetValueOrDefault(CharacterType.Outsider), outsiders);

        if (deltas.Count > 0)
        {
            notes.Add($"镇民随净分布取余量：{baseline.Townsfolk} → {townsfolk}。");
        }

        return notes;
    }

    private static void AddClampNote(
        List<string> notes,
        string label,
        int baseline,
        int delta,
        int final)
    {
        var expected = baseline + delta;
        if (expected == final)
        {
            return;
        }

        notes.Add($"钳制 · {label}：期望 {expected} → 实际 {final}（先加总、再按剧本池与人数约束钳制，R-0042 第 2 条）。");
    }

    private static SetupCounts SelectedCounts(Dictionary<CharacterType, List<SetupPoolEntry>> selected) =>
        new(
            selected[CharacterType.Townsfolk].Count,
            selected[CharacterType.Outsider].Count,
            selected[CharacterType.Minion].Count,
            selected[CharacterType.Demon].Count);

    private static int Clamp(int value, int lower, int upper) => Math.Min(Math.Max(value, lower), upper);

    private static string TypeLabel(CharacterType type) => type switch
    {
        CharacterType.Townsfolk => "镇民",
        CharacterType.Outsider => "外来者",
        CharacterType.Minion => "爪牙",
        CharacterType.Demon => "恶魔",
        _ => type.ToString(),
    };

    /// <summary>Fisher–Yates：把抽取顺序也交给同一条确定性随机流。</summary>
    private static void Shuffle(List<SetupPoolEntry> entries, SeededRandom random)
    {
        for (var i = entries.Count - 1; i > 0; i--)
        {
            var j = random.NextIndex(i + 1);
            (entries[i], entries[j]) = (entries[j], entries[i]);
        }
    }

    /// <summary>
    /// 种子 → 64 位随机流状态：用 SHA-256 而不是 <c>string.GetHashCode</c>
    /// （后者每进程加盐，跨进程不可重放——D-0008 / D-0011 要求显式输入可重放）。
    /// </summary>
    private static ulong SeedState(string seed) =>
        BitConverter.ToUInt64(SHA256.HashData(Encoding.UTF8.GetBytes(seed)), 0);

    /// <summary>splitmix64：跨进程 / 跨平台稳定的确定性随机流。</summary>
    private sealed class SeededRandom(ulong state)
    {
        private ulong _state = state;

        public ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            var z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int NextIndex(int count) => (int)(NextUInt64() % (ulong)count);
    }
}
