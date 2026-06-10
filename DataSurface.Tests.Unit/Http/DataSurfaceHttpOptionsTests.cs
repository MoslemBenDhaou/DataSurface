using DataSurface.Http;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Http;

public class DataSurfaceHttpOptionsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var options = new DataSurfaceHttpOptions();

        options.ApiPrefix.Should().Be("/api");
        options.MapStaticResources.Should().BeTrue();
        // Dynamic catch-all, the discovery endpoint, and ETags are opt-in (must be explicitly enabled).
        options.MapDynamicCatchAll.Should().BeFalse();
        options.DynamicPrefix.Should().Be("/d");
        options.MapResourceDiscoveryEndpoint.Should().BeFalse();
        options.RequireAuthorizationByDefault.Should().BeFalse();
        options.DefaultPolicy.Should().BeNull();
        options.EnableEtags.Should().BeFalse();
        // Route collisions are always configuration errors; failing fast at mapping time is the
        // safe default (otherwise both endpoints map and requests get AmbiguousMatchException 500s).
        options.ThrowOnRouteCollision.Should().BeTrue();
    }

    [Fact]
    public void EnablePutForFullUpdate_DefaultValue_IsFalse()
    {
        var options = new DataSurfaceHttpOptions();

        options.EnablePutForFullUpdate.Should().BeFalse();
    }

    [Fact]
    public void EnablePutForFullUpdate_WhenSet_ReturnsSetValue()
    {
        var options = new DataSurfaceHttpOptions { EnablePutForFullUpdate = true };

        options.EnablePutForFullUpdate.Should().BeTrue();
    }

    [Fact]
    public void EnableImportExport_DefaultValue_IsFalse()
    {
        var options = new DataSurfaceHttpOptions();

        options.EnableImportExport.Should().BeFalse();
    }

    [Fact]
    public void EnableImportExport_WhenSet_ReturnsSetValue()
    {
        var options = new DataSurfaceHttpOptions { EnableImportExport = true };

        options.EnableImportExport.Should().BeTrue();
    }

    [Fact]
    public void EnableRateLimiting_DefaultValue_IsFalse()
    {
        var options = new DataSurfaceHttpOptions();

        options.EnableRateLimiting.Should().BeFalse();
    }

    [Fact]
    public void EnableRateLimiting_WhenSet_ReturnsSetValue()
    {
        var options = new DataSurfaceHttpOptions { EnableRateLimiting = true };

        options.EnableRateLimiting.Should().BeTrue();
    }

    [Fact]
    public void RateLimitingPolicy_DefaultValue_IsNull()
    {
        var options = new DataSurfaceHttpOptions();

        options.RateLimitingPolicy.Should().BeNull();
    }

    [Fact]
    public void RateLimitingPolicy_WhenSet_ReturnsSetValue()
    {
        var options = new DataSurfaceHttpOptions { RateLimitingPolicy = "CustomPolicy" };

        options.RateLimitingPolicy.Should().Be("CustomPolicy");
    }

    [Fact]
    public void EnableApiKeyAuth_DefaultValue_IsFalse()
    {
        var options = new DataSurfaceHttpOptions();

        options.EnableApiKeyAuth.Should().BeFalse();
    }

    [Fact]
    public void EnableApiKeyAuth_WhenSet_ReturnsSetValue()
    {
        var options = new DataSurfaceHttpOptions { EnableApiKeyAuth = true };

        options.EnableApiKeyAuth.Should().BeTrue();
    }

    [Fact]
    public void ApiKeyHeaderName_DefaultValue_IsXApiKey()
    {
        var options = new DataSurfaceHttpOptions();

        options.ApiKeyHeaderName.Should().Be("X-Api-Key");
    }

    [Fact]
    public void ApiKeyHeaderName_WhenSet_ReturnsSetValue()
    {
        var options = new DataSurfaceHttpOptions { ApiKeyHeaderName = "Authorization" };

        options.ApiKeyHeaderName.Should().Be("Authorization");
    }

    [Fact]
    public void AllNewOptions_CanBeSetTogether()
    {
        var options = new DataSurfaceHttpOptions
        {
            EnablePutForFullUpdate = true,
            EnableImportExport = true,
            EnableRateLimiting = true,
            RateLimitingPolicy = "DataSurfacePolicy",
            EnableApiKeyAuth = true,
            ApiKeyHeaderName = "X-Custom-Key"
        };

        options.EnablePutForFullUpdate.Should().BeTrue();
        options.EnableImportExport.Should().BeTrue();
        options.EnableRateLimiting.Should().BeTrue();
        options.RateLimitingPolicy.Should().Be("DataSurfacePolicy");
        options.EnableApiKeyAuth.Should().BeTrue();
        options.ApiKeyHeaderName.Should().Be("X-Custom-Key");
    }
}
