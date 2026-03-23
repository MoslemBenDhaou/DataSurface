using DataSurface.EFCore.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.Tests.Service.Shared.Entities;

/// <summary>
/// In-memory DbContext for CRUD service behavioral tests.
/// </summary>
public class CrudTestDbContext : DbContext
{
    public CrudTestDbContext(DbContextOptions<CrudTestDbContext> options) : base(options) { }

    public DbSet<SimpleItem> SimpleItems => Set<SimpleItem>();
    public DbSet<SoftDeleteItem> SoftDeleteItems => Set<SoftDeleteItem>();
    public DbSet<VersionedItem> VersionedItems => Set<VersionedItem>();
    public DbSet<TenantItem> TenantItems => Set<TenantItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SimpleItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<SoftDeleteItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<VersionedItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<TenantItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });
    }
}

/// <summary>
/// Simple entity for basic CRUD tests.
/// </summary>
public class SimpleItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Entity supporting soft deletion.
/// </summary>
public class SoftDeleteItem : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDeleted { get; set; }
}

/// <summary>
/// Entity with a byte[] concurrency token for concurrency tests.
/// </summary>
public class VersionedItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public byte[] RowVersion { get; set; } = new byte[] { 1, 0, 0, 0 };
}

/// <summary>
/// Entity with a tenant ID for tenant isolation tests.
/// </summary>
public class TenantItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TenantId { get; set; } = "";
    public decimal Price { get; set; }
}
