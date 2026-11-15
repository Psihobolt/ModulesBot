using System.Text.Json;

namespace Modules.MovieNight.Tests.Helpers;

public sealed record TelegramCall(string Method, JsonElement Body);

/// <summary>Фейковый HttpMessageHandler Bot API: записывает метод (последний сегмент пути) и JSON-тело, отвечает ok/true.</summary>
public sealed class FakeTelegramHandler : HttpMessageHandler
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<TelegramCall> _calls = new();

    public IReadOnlyList<TelegramCall> Calls => _calls.ToArray();

    public IReadOnlyList<TelegramCall> Of(string method) => _calls.Where(c => c.Method == method).ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.RequestUri!.AbsolutePath.Split('/').Last();
        var raw = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        _calls.Enqueue(new TelegramCall(method, doc.RootElement.Clone()));
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent("{\"ok\":true,\"result\":true}", System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
