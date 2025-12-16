using DataSurface.Dynamic.Entities;
using Microsoft.EntityFrameworkCore;

namespace DataSurface.Dynamic.Model;

/// <summary>
/// EF Core model builder extensions for DataSurface dynamic metadata tables.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Adds DataSurface dynamic entity metadata and record tables to the model builder.
    /// </summary>
    /// <param name="mb">The model builder.</param>
    /// <param name="schema">The database schema to use.</param>
    /// <returns>The same model builder instance for chaining.</returns>
    public static ModelBuilder AddDataSurfaceDynamic(this ModelBuilder mb, string schema = "dbo")
    {
        mb.Entity<DsEntityDefRow>(b =>
        {
            b.ToTable("DsEntityDef", schema);
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.EntityKey).IsUnique();
            b.HasIndex(x => x.Route).IsUnique();

            b.Property(x => x.EntityKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.Route).HasMaxLength(200).IsRequired();
            b.Property(x => x.KeyName).HasMaxLength(100).IsRequired();

            b.HasMany(x => x.Properties).WithOne(x => x.EntityDef).HasForeignKey(x => x.EntityDefId);
            b.HasMany(x => x.Relations).WithOne(x => x.EntityDef).HasForeignKey(x => x.EntityDefId);
        });

        mb.Entity<DsPropertyDefRow>(b =>
        {
            b.ToTable("DsPropertyDef", schema);
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.EntityDefId, x.ApiName }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.ApiName).HasMaxLength(200).IsRequired();
        });

        mb.Entity<DsRelationDefRow>(b =>
        {
            b.ToTable("DsRelationDef", schema);
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.EntityDefId, x.ApiName }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.ApiName).HasMaxLength(200).IsRequired();
            b.Property(x => x.TargetEntityKey).HasMaxLength(200).IsRequired();
        });

        mb.Entity<DsDynamicRecordRow>(b =>
        {
            b.ToTable("DsDynamicRecord", schema);
            b.HasKey(x => new { x.EntityKey, x.Id }); // partition by entityKey
            b.Property(x => x.Id).HasMaxLength(128).IsRequired();
            b.Property(x => x.EntityKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.DataJson).IsRequired();

            b.Property(x => x.RowVersion).IsRowVersion();
            b.HasIndex(x => new { x.EntityKey, x.IsDeleted });
        });

        mb.Entity<DsDynamicIndexRow>(b =>
        {
            b.ToTable("DsDynamicIndex", schema);
            b.HasKey(x => x.Id);

            b.Property(x => x.EntityKey).HasMaxLength(200).IsRequired();
            b.Property(x => x.RecordId).HasMaxLength(128).IsRequired();
            b.Property(x => x.PropertyApiName).HasMaxLength(200).IsRequired();

            b.HasIndex(x => new { x.EntityKey, x.RecordId });
            b.HasIndex(x => new { x.EntityKey, x.PropertyApiName, x.ValueString });
            b.HasIndex(x => new { x.EntityKey, x.PropertyApiName, x.ValueNumber });
            b.HasIndex(x => new { x.EntityKey, x.PropertyApiName, x.ValueDateTime });
            b.HasIndex(x => new { x.EntityKey, x.PropertyApiName, x.ValueBool });
            b.HasIndex(x => new { x.EntityKey, x.PropertyApiName, x.ValueGuid });
        });

        return mb;
    }
}
