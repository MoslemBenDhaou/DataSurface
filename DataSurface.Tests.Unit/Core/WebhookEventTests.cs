using System.Text.Json.Nodes;
using DataSurface.Core.Enums;
using DataSurface.Core.Webhooks;
using FluentAssertions;
using Xunit;

namespace DataSurface.Tests.Unit.Core;

public class WebhookEventTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var payload = new JsonObject { ["id"] = 1, ["name"] = "Test" };
        var timestamp = DateTime.UtcNow;
        
        var evt = new WebhookEvent(
            ResourceKey: "User",
            Operation: CrudOperation.Create,
            EntityId: "123",
            Payload: payload,
            Timestamp: timestamp
        );
        
        evt.ResourceKey.Should().Be("User");
        evt.Operation.Should().Be(CrudOperation.Create);
        evt.EntityId.Should().Be("123");
        evt.Payload.Should().NotBeNull();
        evt.Payload!["id"]!.GetValue<int>().Should().Be(1);
        evt.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void Constructor_ForDeleteOperation_HasNullPayload()
    {
        var evt = new WebhookEvent(
            ResourceKey: "User",
            Operation: CrudOperation.Delete,
            EntityId: "456",
            Payload: null,
            Timestamp: DateTime.UtcNow
        );
        
        evt.Operation.Should().Be(CrudOperation.Delete);
        evt.EntityId.Should().Be("456");
        evt.Payload.Should().BeNull();
    }

    [Fact]
    public void Constructor_ForListOperation_HasNullEntityId()
    {
        var evt = new WebhookEvent(
            ResourceKey: "User",
            Operation: CrudOperation.List,
            EntityId: null,
            Payload: null,
            Timestamp: DateTime.UtcNow
        );
        
        evt.Operation.Should().Be(CrudOperation.List);
        evt.EntityId.Should().BeNull();
    }
}

public class WebhookSubscriptionTests
{
    [Fact]
    public void Constructor_WithAllParameters_SetsAllProperties()
    {
        var operations = new[] { CrudOperation.Create, CrudOperation.Update };
        
        var subscription = new WebhookSubscription(
            Id: "sub-123",
            Url: "https://example.com/webhook",
            ResourceKey: "User",
            Operations: operations,
            Secret: "secret-key",
            IsActive: true
        );
        
        subscription.Id.Should().Be("sub-123");
        subscription.Url.Should().Be("https://example.com/webhook");
        subscription.ResourceKey.Should().Be("User");
        subscription.Operations.Should().BeEquivalentTo(operations);
        subscription.Secret.Should().Be("secret-key");
        subscription.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullFilters_MatchesAllResources()
    {
        var subscription = new WebhookSubscription(
            Id: "sub-456",
            Url: "https://example.com/webhook",
            ResourceKey: null,
            Operations: null,
            Secret: null,
            IsActive: true
        );
        
        subscription.ResourceKey.Should().BeNull();
        subscription.Operations.Should().BeNull();
        subscription.Secret.Should().BeNull();
    }

    [Fact]
    public void IsActive_WhenFalse_IndicatesInactiveSubscription()
    {
        var subscription = new WebhookSubscription(
            Id: "sub-789",
            Url: "https://example.com/webhook",
            ResourceKey: null,
            Operations: null,
            Secret: null,
            IsActive: false
        );
        
        subscription.IsActive.Should().BeFalse();
    }
}
