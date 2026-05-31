using System.Security.Claims;
using DataSurface.EFCore.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DataSurface.Http;

/// <summary>
/// Dependency injection registration helpers for the DataSurface HTTP layer.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers HTTP-layer services that the DataSurface endpoints rely on.
    /// </summary>
    /// <remarks>
    /// Registers <see cref="IHttpContextAccessor"/> and bridges the current request's
    /// <see cref="ClaimsPrincipal"/> (<c>HttpContext.User</c>) into DI so that claim-based
    /// tenant isolation in the EF Core layer (<c>CrudSecurityDispatcher</c>) works out of the
    /// box. Register a custom <see cref="ITenantResolver"/> to override tenant resolution.
    /// </remarks>
    /// <param name="services">The service collection to register into.</param>
    /// <returns>The same <paramref name="services"/> instance for chaining.</returns>
    public static IServiceCollection AddDataSurfaceHttp(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        // ASP.NET Core does not register ClaimsPrincipal in DI (it lives on HttpContext.User).
        // Bridge it here so tenant/claim resolution has a principal to read from.
        services.TryAddScoped<ClaimsPrincipal>(sp =>
            sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.User
            ?? new ClaimsPrincipal(new ClaimsIdentity()));

        return services;
    }
}
