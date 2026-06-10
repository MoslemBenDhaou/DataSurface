using DataSurface.Dynamic.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DataSurface.Dynamic.DI;

/// <summary>
/// Hosted service that loads all dynamic entity definitions into the shared contract cache at
/// application startup, honoring <see cref="DataSurfaceDynamicOptions.WarmUpContractsOnStart"/>.
/// </summary>
internal sealed class DynamicContractWarmupHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DynamicContractWarmupHostedService> _logger;

    public DynamicContractWarmupHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DynamicContractWarmupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var provider = scope.ServiceProvider.GetRequiredService<DynamicResourceContractProvider>();
            await provider.WarmUpAsync(cancellationToken);
            _logger.LogInformation("Dynamic contract warm-up loaded {Count} contract(s).", provider.All.Count);
        }
        catch (Exception ex)
        {
            // Warm-up is an optimization: a failure (e.g. database not migrated yet) must not
            // prevent startup; contracts will be loaded lazily on first use instead.
            _logger.LogWarning(ex, "Dynamic contract warm-up failed; contracts will be loaded lazily.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
