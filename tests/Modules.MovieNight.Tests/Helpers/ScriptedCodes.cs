using System.Collections.Concurrent;
using System.Globalization;
using Modules.MovieNight.Application;

namespace Modules.MovieNight.Tests.Helpers;

/// <summary>
/// IInviteCodeGenerator с заданной последовательностью (Enqueue); когда очередь пуста — уникальные
/// детерминированные коды "Code000001", "Code000002", ...
/// </summary>
public sealed class ScriptedCodes : IInviteCodeGenerator
{
    private readonly ConcurrentQueue<string> _queue = new();
    private int _counter;

    public int Calls => Volatile.Read(ref _calls);

    private int _calls;

    public void Enqueue(params string[] codes)
    {
        foreach (var c in codes) _queue.Enqueue(c);
    }

    public string Next()
    {
        Interlocked.Increment(ref _calls);
        return _queue.TryDequeue(out var code)
            ? code
            : "Code" + Interlocked.Increment(ref _counter).ToString("D6", CultureInfo.InvariantCulture);
    }
}
