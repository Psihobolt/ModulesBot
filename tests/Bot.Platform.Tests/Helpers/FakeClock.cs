using Bot.Platform.Time;

namespace Bot.Platform.Tests.Helpers;

public sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = TestInitData.DefaultNow;

    public List<TimeSpan> Delays { get; } = [];

    public void Advance(TimeSpan by) => UtcNow += by;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        Delays.Add(delay);
        return Task.CompletedTask;
    }
}
