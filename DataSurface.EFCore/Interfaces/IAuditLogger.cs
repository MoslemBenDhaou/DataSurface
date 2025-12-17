using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.EFCore.Interfaces;

/// <summary>
/// Provides audit logging for CRUD operations.
/// </summary>
/// <remarks>
/// Implement this interface to log all data access and modifications for compliance and security auditing.
/// </remarks>
/// <example>
/// <code>
/// public class DatabaseAuditLogger : IAuditLogger
/// {
///     private readonly AppDbContext _db;
///     private readonly IHttpContextAccessor _http;
///     
///     public DatabaseAuditLogger(AppDbContext db, IHttpContextAccessor http)
///     {
///         _db = db;
///         _http = http;
///     }
///     
///     public async Task LogAsync(AuditLogEntry entry, CancellationToken ct)
///     {
///         _db.AuditLogs.Add(new AuditLog
///         {
///             UserId = _http.HttpContext?.User.FindFirst("sub")?.Value,
///             Operation = entry.Operation.ToString(),
///             ResourceKey = entry.ResourceKey,
///             EntityId = entry.EntityId,
///             Timestamp = entry.Timestamp,
///             Changes = entry.Changes?.ToJsonString()
///         });
///         await _db.SaveChangesAsync(ct);
///     }
/// }
/// </code>
/// </example>
public interface IAuditLogger
{
    /// <summary>
    /// Logs an audit entry for a CRUD operation.
    /// </summary>
    /// <param name="entry">The audit log entry.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the log has been recorded.</returns>
    Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents an audit log entry for a CRUD operation.
/// </summary>
public sealed record AuditLogEntry
{
    /// <summary>
    /// Gets or sets the CRUD operation that was performed.
    /// </summary>
    public required CrudOperation Operation { get; init; }

    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public required string ResourceKey { get; init; }

    /// <summary>
    /// Gets or sets the entity ID (if applicable).
    /// </summary>
    public string? EntityId { get; init; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the operation.
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the user identifier who performed the operation.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Gets or sets the IP address of the client.
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Gets or sets whether the operation succeeded.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Gets or sets the error message if the operation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets or sets the changes made (for create/update operations).
    /// </summary>
    public JsonObject? Changes { get; init; }

    /// <summary>
    /// Gets or sets the previous values (for update operations).
    /// </summary>
    public JsonObject? PreviousValues { get; init; }
}
