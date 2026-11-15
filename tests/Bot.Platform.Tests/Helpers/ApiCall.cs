using System.Net.Http.Json;
using System.Text.Json;

namespace Bot.Platform.Tests.Helpers;

public static class ApiCall
{
    public static string Tma(string rawInitData) => "tma " + rawInitData;

    /// <summary>GET with a raw Authorization header value (null = no header).</summary>
    public static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path, string? authorization = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (authorization is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        return await client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> GetAsUserAsync(HttpClient client, string path, long userId, DateTimeOffset? authDate = null) =>
        GetAsync(client, path, Tma(TestInitData.For(userId, authDate)));

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Asserts status + ProblemDetails "code" and returns the parsed body.</summary>
    public static async Task<JsonElement> AssertProblemAsync(HttpResponseMessage response, int status, string code)
    {
        Assert.Equal(status, (int)response.StatusCode);
        var body = await JsonAsync(response);
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }
}
