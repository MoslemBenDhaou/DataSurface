using DataSurface.EFCore.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DataSurface.Http;

/// <summary>
/// Maps an endpoint that exposes the available DataSurface resources and their capabilities.
/// </summary>
public static class DataSurfaceResourceDiscovery
{
    /// <summary>
    /// Maps the discovery endpoint under the provided route group.
    /// </summary>
    /// <param name="group">The route group to register the endpoint on.</param>
    public static void MapDiscovery(RouteGroupBuilder group)
    {
        group.MapGet("/$resources", (IResourceContractProvider provider) =>
        {
            var list = provider.All.Select(c => new
            {
                c.ResourceKey,
                c.Route,
                Backend = c.Backend.ToString(),
                Ops = c.Operations
                    .Where(kv => kv.Value.Enabled)
                    .Select(kv => kv.Key.ToString())
                    .ToArray(),
                Read = new
                {
                    Fields = c.Fields.Where(f => f.InRead && !f.Hidden).Select(f => f.ApiName).ToArray(),
                    ExpandAllowed = c.Read.ExpandAllowed.ToArray()
                }
            });

            return Results.Ok(list);
        })
        .WithName("DataSurface.Resources")
        .WithTags("DataSurface");
    }
}
