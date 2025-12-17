using DataSurface.EFCore.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DataSurface.EFCore.Observability;

/// <summary>
/// Health check for the EF Core database connectivity.
/// </summary>
/// <remarks>
/// <para>
/// This health check verifies that the database is reachable and responsive.
/// Register it with ASP.NET Core health checks:
/// </para>
/// <code>
/// builder.Services.AddHealthChecks()
///     .AddCheck&lt;DataSurfaceDbHealthCheck&gt;("datasurface-db");
/// </code>
/// </remarks>
public class DataSurfaceDbHealthCheck : IHealthCheck
{
    private readonly DbContext _db;

    /// <summary>
    /// Creates a new health check instance.
    /// </summary>
    /// <param name="db">The database context to check.</param>
    public DataSurfaceDbHealthCheck(DbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Attempt a simple query to verify connectivity
            var canConnect = await _db.Database.CanConnectAsync(cancellationToken);
            
            if (canConnect)
            {
                return HealthCheckResult.Healthy("Database connection is healthy.");
            }

            return HealthCheckResult.Unhealthy("Cannot connect to database.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Database health check failed.",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

/// <summary>
/// Health check for DataSurface resource contracts.
/// </summary>
/// <remarks>
/// <para>
/// This health check verifies that resource contracts are properly loaded and available.
/// </para>
/// <code>
/// builder.Services.AddHealthChecks()
///     .AddCheck&lt;DataSurfaceContractsHealthCheck&gt;("datasurface-contracts");
/// </code>
/// </remarks>
public class DataSurfaceContractsHealthCheck : IHealthCheck
{
    private readonly IResourceContractProvider _contracts;

    /// <summary>
    /// Creates a new health check instance.
    /// </summary>
    /// <param name="contracts">The contract provider to check.</param>
    public DataSurfaceContractsHealthCheck(IResourceContractProvider contracts)
    {
        _contracts = contracts;
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var contracts = _contracts.All.ToList();
            var count = contracts.Count;

            if (count == 0)
            {
                return Task.FromResult(HealthCheckResult.Degraded(
                    "No resource contracts are registered.",
                    data: new Dictionary<string, object>
                    {
                        { "contract_count", 0 }
                    }));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"{count} resource contract(s) loaded.",
                data: new Dictionary<string, object>
                {
                    { "contract_count", count },
                    { "resources", contracts.Select(c => c.ResourceKey).ToArray() }
                }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Failed to retrieve resource contracts.",
                exception: ex));
        }
    }
}
