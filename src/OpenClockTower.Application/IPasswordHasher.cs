namespace OpenClockTower.Application;

/// <summary>
/// 口令 / 恢复码哈希器（D-0021）：随机盐、慢哈希、固定时间比较。
/// </summary>
/// <remarks>
/// 明文口令与恢复码只在<b>调用栈内</b>存在：本接口只产出 / 校验自描述哈希串，
/// 调用方不得把明文写进日志、事件或视图。
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>把秘密（口令 / 恢复码）哈希成自描述串（含算法、迭代次数与随机盐）。</summary>
    string Hash(string secret);

    /// <summary>固定时间比较；哈希串为空 / 格式不识别时返回 false（不抛异常）。</summary>
    bool Verify(string secret, string storedHash);
}
