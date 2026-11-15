using System.Net;
using Bot.Platform.Tests.Helpers;
using Microsoft.Extensions.Logging;

namespace Bot.Platform.Tests;

// TEST-5: AC-14, AC-15
public sealed class StaticFilesTests : IDisposable
{
    private const string IndexMarker = "INDEX-HTML-MARKER-7f3a";
    private readonly string _dist = Path.Combine(TempDir.Create(), "dist");

    public StaticFilesTests()
    {
        Directory.CreateDirectory(Path.Combine(_dist, "assets"));
        File.WriteAllText(Path.Combine(_dist, "index.html"), $"<!doctype html><html><body>{IndexMarker}</body></html>");
        File.WriteAllText(Path.Combine(_dist, "assets", "app-abc123.js"), "console.log('app');");
    }

    public void Dispose() => TempDir.TryDelete(Path.GetDirectoryName(_dist)!);

    private static string CacheControl(HttpResponseMessage response) =>
        string.Join(", ", response.Headers.TryGetValues("Cache-Control", out var v) ? v : []);

    [Theory]
    [InlineData("/")]
    [InlineData("/movienight/groups/1")]
    [InlineData("/some/deep/spa/route")]
    public async Task AC14_PathOutsideApi_ReturnsIndexHtmlWithNoCache(string path)
    {
        await using var factory = new PlatformFactory { DistPath = _dist };
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(IndexMarker, await response.Content.ReadAsStringAsync());
        Assert.Contains("no-cache", CacheControl(response));
    }

    [Fact]
    public async Task AC14_ExplicitIndexHtml_ReturnsNoCache()
    {
        await using var factory = new PlatformFactory { DistPath = _dist };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-cache", CacheControl(response));
    }

    [Fact]
    public async Task AC14_HashedAsset_ReturnsContentWithImmutableCache()
    {
        await using var factory = new PlatformFactory { DistPath = _dist };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/assets/app-abc123.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("console.log('app');", await response.Content.ReadAsStringAsync());
        var cache = CacheControl(response);
        Assert.Contains("public", cache);
        Assert.Contains("max-age=31536000", cache);
        Assert.Contains("immutable", cache);
    }

    [Theory]
    [InlineData("/assets/missing.js")]
    [InlineData("/assets/foo")]
    [InlineData("/missing.png")]
    [InlineData("/movienight/file.txt")]
    public async Task AC14_MissingFileLikeOrAssetPath_Returns404NotIndexHtml(string path)
    {
        await using var factory = new PlatformFactory { DistPath = _dist };
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(IndexMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AC14_HealthAndApiAreNotShadowedByFallback()
    {
        await using var factory = new PlatformFactory { DistPath = _dist };
        var client = factory.CreateClient();

        var health = await client.GetAsync("/health");
        var api = await client.GetAsync("/api/platform/me");
        var apiUnknown = await ApiCall.GetAsync(client, "/api/nothing", ApiCall.Tma(TestInitData.For(42, factory.Clock.UtcNow)));

        Assert.DoesNotContain(IndexMarker, await health.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, api.StatusCode);
        Assert.DoesNotContain(IndexMarker, await api.Content.ReadAsStringAsync());
        await ApiCall.AssertProblemAsync(apiUnknown, 404, "not_found");
    }

    [Fact]
    public async Task AC14_BotPrefix_IsNotServedAsIndexHtml()
    {
        await using var factory = new PlatformFactory { DistPath = _dist };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/bot/anything");

        Assert.DoesNotContain(IndexMarker, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AC15_DistFolderMissing_StartsLogsWarningRootIs404ApiAndHealthWork()
    {
        var missing = Path.Combine(Path.GetTempPath(), "bot-platform-tests", Guid.NewGuid().ToString("N"), "nope");
        await using var factory = new PlatformFactory { DistPath = missing };
        var client = factory.CreateClient();

        var root = await client.GetAsync("/");
        var health = await client.GetAsync("/health");
        var me = await ApiCall.GetAsync(client, "/api/platform/me", ApiCall.Tma(TestInitData.For(42, factory.Clock.UtcNow)));

        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Contains(factory.Logs.Entries, e =>
            e.Level == LogLevel.Warning
            && (e.FullText.Contains(missing, StringComparison.Ordinal) || e.FullText.Contains("DistPath", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task AC15_DistFolderMissing_DeepSpaPathIs404()
    {
        var missing = Path.Combine(Path.GetTempPath(), "bot-platform-tests", Guid.NewGuid().ToString("N"), "nope");
        await using var factory = new PlatformFactory { DistPath = missing };
        var client = factory.CreateClient();

        var response = await client.GetAsync("/movienight/groups/1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
