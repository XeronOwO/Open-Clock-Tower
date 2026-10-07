using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OpenClockTower.Integration.Tests;

/// <summary>把宿主日志收进内存队列：负向套件用它断言"拒绝都留下了可定位的审计"（矩阵行 11）。</summary>
internal sealed class CollectingLoggerProvider(ConcurrentQueue<string> sink) : ILoggerProvider
{
    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new CollectingLogger(categoryName, sink);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>极简 logger：只把格式化后的行塞进队列，不做任何过滤之外的加工。</summary>
    private sealed class CollectingLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            sink.Enqueue(
                exception is null
                    ? $"[{logLevel}] {category}: {formatter(state, exception)}"
                    : $"[{logLevel}] {category}: {formatter(state, exception)} | 异常：{exception.GetType().Name}: {exception.Message}");
    }
}
