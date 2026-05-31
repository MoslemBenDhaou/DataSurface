using DataSurface.Scalar;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DataSurface.Tests.Service.Scalar;

/// <summary>
/// Verifies the Scalar API-reference UI integration: <c>MapDataSurfaceScalar</c> maps a working
/// endpoint that serves the Scalar reference UI (task #4).
/// </summary>
public class ScalarEndpointTests
{
    [Fact]
    public async Task MapDataSurfaceScalar_ServesScalarReferenceUi()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.MapDataSurfaceScalar();

        await app.StartAsync();
        var client = app.GetTestClient();

        // Scalar serves the UI under the configured prefix (the exact route may include a document
        // segment and/or a trailing-slash redirect), so probe the candidates and follow redirects.
        string[] candidates = { "/scalar", "/scalar/", "/scalar/v1" };
        var statuses = new List<string>();
        HttpResponseMessage? ok = null;

        foreach (var path in candidates)
        {
            var response = await GetFollowingRedirectsAsync(client, path);
            statuses.Add($"{path} -> {(int)response.StatusCode}");
            if (response.IsSuccessStatusCode)
            {
                ok = response;
                break;
            }
        }

        ok.Should().NotBeNull($"the Scalar reference UI should be served under /scalar. Tried: {string.Join(", ", statuses)}");

        var html = await ok!.Content.ReadAsStringAsync();
        html.ToLowerInvariant().Should().Contain("scalar", "the response should be the Scalar reference UI");

        await app.DisposeAsync();
    }

    private static async Task<HttpResponseMessage> GetFollowingRedirectsAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);

        var hops = 0;
        while ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location && hops++ < 5)
        {
            var next = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
            response = await client.GetAsync(next);
        }

        return response;
    }
}
