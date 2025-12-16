using System.Collections.Concurrent;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DataSurface.EFCore.Context;

/// <summary>
/// Base <see cref="Microsoft.EntityFrameworkCore.DbContext"/> that applies DataSurface conventions and optionally
/// auto-registers resource entities.
/// </summary>
/// <typeparam name="TContext">The concrete <see cref="Microsoft.EntityFrameworkCore.DbContext"/> type.</typeparam>
/// <remarks>
/// This context uses <see cref="IResourceContractProvider"/> to discover resources and can:
/// - register entity types for resources backed by <c>StorageBackend.EfCore</c>
/// - apply a soft-delete query filter
/// - configure <c>RowVersion</c> properties as rowversion concurrency tokens
/// </remarks>
public abstract class DeclarativeDbContext<TContext> : DbContext
    where TContext : DbContext
{
    private readonly DataSurfaceEfCoreOptions _opt;
    private readonly IResourceContractProvider _contracts;

    /// <summary>
    /// Initializes a new declarative DbContext.
    /// </summary>
    /// <param name="options">EF Core options for the concrete context.</param>
    /// <param name="opt">DataSurface EF Core integration options.</param>
    /// <param name="contracts">Provider for the resource contract set used during model building.</param>
    protected DeclarativeDbContext(
        DbContextOptions<TContext> options,
        DataSurfaceEfCoreOptions opt,
        IResourceContractProvider contracts) : base(options)
    {
        _opt = opt;
        _contracts = contracts;
    }

    /// <summary>
    /// Builds the EF model and applies DataSurface conventions.
    /// </summary>
    /// <param name="modelBuilder">Model builder used to configure the EF model.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (_opt.AutoRegisterCrudEntities)
        {
            foreach (var c in _contracts.All.Where(c => c.Backend == StorageBackend.EfCore))
            {
                // EF needs CLR types; for static scanning we assume resourceKey == CLR type name
                // (If you want explicit mapping later: add a registry map ResourceKey -> CLR Type)
                var clrType = ResolveClrType(c.ResourceKey);
                modelBuilder.Entity(clrType);
            }
        }

        if (_opt.EnableSoftDeleteFilter)
        {
            ApplySoftDeleteConvention(modelBuilder);
        }

        if (_opt.EnableRowVersionConvention)
        {
            ApplyRowVersionConvention(modelBuilder);
        }
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTimestamps()
    {
        if (!_opt.EnableTimestampConvention) return;

        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<ITimestamped>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }

    // Static cache for CLR type resolution to avoid scanning assemblies repeatedly
    private static readonly ConcurrentDictionary<string, Type> _typeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves a CLR type for a given resource key.
    /// </summary>
    /// <param name="resourceKey">The contract resource key.</param>
    /// <returns>The CLR type to register in the EF model.</returns>
    /// <remarks>
    /// The default implementation searches loaded assemblies for a type whose name matches <paramref name="resourceKey"/>
    /// (case-insensitive). Override this method to provide explicit mappings.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the CLR type cannot be resolved.</exception>
    protected virtual Type ResolveClrType(string resourceKey)
    {
        return _typeCache.GetOrAdd(resourceKey, key =>
        {
            var t = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => SafeGetTypes(a))
                .FirstOrDefault(x => x.Name.Equals(key, StringComparison.OrdinalIgnoreCase));

            return t ?? throw new InvalidOperationException(
                $"Cannot resolve CLR type for resourceKey '{key}'. " +
                $"Override ResolveClrType() or add an explicit mapping registry.");
        });
    }

    private static IEnumerable<Type> SafeGetTypes(System.Reflection.Assembly a)
    {
        try { return a.GetTypes(); }
        catch { return Array.Empty<Type>(); }
    }

    private static void ApplySoftDeleteConvention(ModelBuilder modelBuilder)
    {
        foreach (var et in modelBuilder.Model.GetEntityTypes())
        {
            var clr = et.ClrType;
            if (typeof(ISoftDelete).IsAssignableFrom(clr))
            {
                // query filter: IsDeleted == false
                var param = System.Linq.Expressions.Expression.Parameter(clr, "e");
                var prop = System.Linq.Expressions.Expression.Property(param, nameof(ISoftDelete.IsDeleted));
                var body = System.Linq.Expressions.Expression.Equal(prop, System.Linq.Expressions.Expression.Constant(false));
                var lambda = System.Linq.Expressions.Expression.Lambda(body, param);
                modelBuilder.Entity(clr).HasQueryFilter(lambda);
            }
        }
    }

    private static void ApplyRowVersionConvention(ModelBuilder modelBuilder)
    {
        foreach (var et in modelBuilder.Model.GetEntityTypes())
        {
            var clr = et.ClrType;
            var rv = clr.GetProperty("RowVersion");
            if (rv != null && rv.PropertyType == typeof(byte[]))
            {
                modelBuilder.Entity(clr).Property("RowVersion").IsRowVersion();
            }
        }
    }
}
