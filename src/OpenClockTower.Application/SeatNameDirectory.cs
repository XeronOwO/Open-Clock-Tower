using OpenClockTower.Kernel;

namespace OpenClockTower.Application;

/// <summary>
/// 席位名读模型（D-0021）：当前「席位 → 账号 + 玩家名」的**唯一内存副本**，供视图投影同步读取。
/// </summary>
/// <remarks>
/// <para>
/// 数据源是账号表 + 席位绑定表（会话信息，不进事件流）：启动时装载，认领 / 改名 / 解除时更新。
/// 改名是**读时解析**——全盘显示账号当前玩家名，不保存逐步骤历史名（D-0021 的明账）。
/// </para>
/// <para>
/// 单一所有者：可变字典由本类内部持有，只暴露窄接口（装载 / 登记 / 移除 / 改名 / 快照）；
/// 快照按席位升序，调用方不得缓存它当第二份事实来源。
/// </para>
/// </remarks>
public sealed class SeatNameDirectory
{
    private readonly object _gate = new();
    private readonly Dictionary<SeatId, Entry> _seats = [];

    /// <summary>全量装载：按绑定关系与账号当前玩家名替换整份读模型。</summary>
    /// <param name="bindings">本局全部席位绑定。</param>
    /// <param name="displayNames">账号 → 玩家名（账号已不存在时不出现）。</param>
    public void Load(IEnumerable<SeatBinding> bindings, IReadOnlyDictionary<AccountId, string> displayNames)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(displayNames);

        lock (_gate)
        {
            _seats.Clear();
            foreach (var binding in bindings)
            {
                if (displayNames.TryGetValue(binding.AccountId, out var displayName) && displayName.Length > 0)
                {
                    _seats[binding.Seat] = new Entry(binding.AccountId, displayName);
                }
            }
        }
    }

    /// <summary>启动装载：读绑定表 + 账号表，把读模型换成当前事实。</summary>
    public async Task ReloadAsync(
        GameId gameId,
        ISeatBindingStore bindings,
        IAccountStore accounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(accounts);

        var rows = await bindings.ListByGameAsync(gameId, cancellationToken);
        var displayNames = new Dictionary<AccountId, string>();
        foreach (var accountId in rows.Select(row => row.AccountId).Distinct())
        {
            if (await accounts.FindByIdAsync(accountId, cancellationToken) is { } account)
            {
                displayNames[accountId] = account.DisplayName;
            }
        }

        Load(rows, displayNames);
    }

    /// <summary>认领：登记一个席位的玩家名（首次加入即绑定，D-0021）。</summary>
    public void Set(SeatId seat, AccountId accountId, string displayName)
    {
        ArgumentException.ThrowIfNullOrEmpty(displayName);

        lock (_gate)
        {
            _seats[seat] = new Entry(accountId, displayName);
        }
    }

    /// <summary>解除绑定：移除一个席位（说书人兜底）。</summary>
    public void Remove(SeatId seat)
    {
        lock (_gate)
        {
            _seats.Remove(seat);
        }
    }

    /// <summary>账号改名：更新该账号在本局的全部席位（改名即时生效，D-0021）。</summary>
    public void Rename(AccountId accountId, string displayName)
    {
        ArgumentException.ThrowIfNullOrEmpty(displayName);

        lock (_gate)
        {
            foreach (var seat in _seats.Where(pair => pair.Value.AccountId == accountId).Select(pair => pair.Key).ToArray())
            {
                _seats[seat] = new Entry(accountId, displayName);
            }
        }
    }

    /// <summary>某席位的玩家名；没有绑定 / 没有名字返回 null（呈现层回退「N 号」）。</summary>
    public string? NameOf(SeatId seat)
    {
        lock (_gate)
        {
            return _seats.TryGetValue(seat, out var entry) ? entry.DisplayName : null;
        }
    }

    /// <summary>按席位升序的快照；视图投影只读这一份。</summary>
    public IReadOnlyList<SeatDisplayName> Snapshot()
    {
        lock (_gate)
        {
            return
            [
                .. _seats
                    .OrderBy(pair => pair.Key.Value)
                    .Select(pair => new SeatDisplayName { Seat = pair.Key, DisplayName = pair.Value.DisplayName }),
            ];
        }
    }

    /// <summary>一条读模型记录：账号标识 + 玩家名。</summary>
    private sealed record Entry(AccountId AccountId, string DisplayName);
}
