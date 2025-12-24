using System.Text.Json.Nodes;
using DataSurface.Core.Enums;

namespace DataSurface.Core.Webhooks;

/// <summary>
/// Interface for publishing webhook events when CRUD operations occur.
/// </summary>
public interface IWebhookPublisher
{
    /// <summary>
    /// Publishes a webhook event for a CRUD operation.
    /// </summary>
    /// <param name="event">The webhook event to publish.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PublishAsync(WebhookEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Represents a webhook event triggered by a CRUD operation.
/// </summary>
/// <param name="ResourceKey">The resource key that triggered the event.</param>
/// <param name="Operation">The CRUD operation that occurred.</param>
/// <param name="EntityId">The ID of the affected entity (null for List operations).</param>
/// <param name="Payload">The entity data (for Create/Update operations).</param>
/// <param name="Timestamp">When the event occurred.</param>
public sealed record WebhookEvent(
    string ResourceKey,
    CrudOperation Operation,
    string? EntityId,
    JsonObject? Payload,
    DateTime Timestamp
);

/// <summary>
/// Webhook subscription configuration.
/// </summary>
/// <param name="Id">Unique subscription ID.</param>
/// <param name="Url">The URL to send webhook events to.</param>
/// <param name="ResourceKey">Optional filter by resource key (null = all resources).</param>
/// <param name="Operations">Optional filter by operations (null = all operations).</param>
/// <param name="Secret">Optional secret for HMAC signature verification.</param>
/// <param name="IsActive">Whether the subscription is active.</param>
public sealed record WebhookSubscription(
    string Id,
    string Url,
    string? ResourceKey,
    IReadOnlyList<CrudOperation>? Operations,
    string? Secret,
    bool IsActive
);
