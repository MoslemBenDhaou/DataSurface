using DataSurface.Dynamic.Contracts;
using DataSurface.Dynamic.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DataSurface.Dynamic.Observability;

/// <summary>
/// Health check for the dynamic metadata store.
/// </summary>
/// <remarks>
/// <para>
/// This health check verifies that the dynamic entity definitions table is accessible
/// and returns the count of registered dynamic entities.
/// </para>
/// <code>
/// builder.Services.AddHealthChecks()
///     .AddCheck&lt;DynamicMetadataHealthCheck&gt;("datasurface-dynamic-metadata");
/// </code>
/// </remarks>
public class DynamicMetadataHealthCheck : IHealthCheck
{
    private readonly DbContext _db;

    /// <summary>
    /// Creates a new health check instance.
    /// </summary>
    /// <param name="db">The database context containing dynamic metadata tables.</param>
    public DynamicMetadataHealthCheck(DbContext db)
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
            // Try to query the dynamic entity definitions
            var entitySet = _db.Set<DsEntityDefRow>();
            var count = await entitySet.CountAsync(cancellationToken);

            return HealthCheckResult.Healthy(
                $"Dynamic metadata store is healthy. {count} entity definition(s) registered.",
                data: new Dictionary<string, object>
                {
                    { "entity_count", count }
                });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Dynamic metadata store health check failed.",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}

/// <summary>
/// Health check for the dynamic contracts provider.
/// </summary>
/// <remarks>
/// <para>
/// This health check verifies that dynamic contracts are properly loaded and cached.
/// </para>
/// <code>
/// builder.Services.AddHealthChecks()
///     .AddCheck&lt;DynamicContractsHealthCheck&gt;("datasurface-dynamic-contracts");
/// </code>
/// </remarks>
public class DynamicContractsHealthCheck : IHealthCheck
{
    private readonly DynamicResourceContractProvider _provider;

    /// <summary>
    /// Creates a new health check instance.
    /// </summary>
    /// <param name="provider">The dynamic contract provider to check.</param>
    public DynamicContractsHealthCheck(DynamicResourceContractProvider provider)
    {
        _provider = provider;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Warm up contracts to ensure they're loaded
            await _provider.WarmUpAsync(cancellationToken);

            var contracts = _provider.All.ToList();
            var count = contracts.Count;

            var data = new Dictionary<string, object>
            {
                { "contract_count", count },
                { "resources", contracts.Select(c => c.ResourceKey).ToArray() }
            };

            if (count == 0)
            {
                return HealthCheckResult.Healthy(
                    "Dynamic contracts provider is healthy. No dynamic entities registered yet.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Dynamic contracts provider is healthy. {count} contract(s) loaded.",
                data: data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Dynamic contracts provider health check failed.",
                exception: ex,
                data: new Dictionary<string, object>
                {
                    { "error", ex.Message }
                });
        }
    }
}
