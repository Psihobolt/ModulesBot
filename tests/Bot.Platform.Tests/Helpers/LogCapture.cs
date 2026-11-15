using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Bot.Platform.Tests.Helpers;

public sealed record LogEntry(LogLevel Level, string Category, string Message, Exception? Exception)
{
    /// <summary>Everything that could leak: message, structured values, exception with stack trace.</summary>
    public string FullText => Message + "\n" + Exception;
}

public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CaptureLogger(string category, ConcurrentQueue<LogEntry> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (state is IEnumerable<KeyValuePair<string, object?>> values)
            {
                message += " | " + string.Join(", ", values.Select(v => $"{v.Key}={v.Value}"));
            }

            sink.Enqueue(new LogEntry(logLevel, category, message, exception));
        }
    }
}
