using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Dynamic.DI;
using DataSurface.Dynamic.Entities;
using DataSurface.Dynamic.Model;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.Dynamic;

/// <summary>
/// Verifies that field-level authorization is enforced on the dynamic CRUD path (B6 "no deferral"),
/// reached through the backend router (<see cref="IDataSurfaceCrudService"/>).
/// </summary>
public class DynamicAuthorizationTests
{
    private sealed class DynDbContext : DbContext
    {
        public DynDbContext(DbContextOptions<DynDbContext> o) : base(o) { }
        protected override void OnModelCreating(ModelBuilder mb)
        {
            mb.AddDataSurfaceDynamic();
            mb.Entity<DsDynamicRecordRow>().Property(x => x.RowVersion).IsRequired(false).ValueGeneratedNever();
        }
    }

    // Field authorizer that forbids writing the "name" field.
    private sealed class NoNameWriteAuthorizer : IFieldAuthorizer
    {
        public bool CanReadField(ResourceContract contract, string fieldName) => true;
        public bool CanWriteField(ResourceContract contract, string fieldName, CrudOperation operation)
            => !fieldName.Equals("name", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DynamicCreate_FieldWriteAuthorization_IsEnforced()
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<DynDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<DynDbContext>());
        services.AddSingleton<IFieldAuthorizer>(new NoNameWriteAuthorizer());
        services.AddDataSurfaceEfCore(_ => { });
        services.AddDataSurfaceDynamic();

        using var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DynDbContext>();
            db.Database.EnsureCreated();
            db.Set<DsEntityDefRow>().Add(new DsEntityDefRow
            {
                EntityKey = "Widget",
                Route = "widgets",
                Backend = StorageBackend.DynamicJson,
                KeyName = "id",
                KeyType = FieldType.Guid,
                Properties = new List<DsPropertyDefRow>
                {
                    new() { Name = "name", ApiName = "name", Type = FieldType.String, InFlags = CrudDto.Read | CrudDto.Create | CrudDto.Update, RequiredOnCreate = true }
                }
            });
            await db.SaveChangesAsync();
        }

        using (var scope = provider.CreateScope())
        {
            // Resolved via the router (IDataSurfaceCrudService) -> dynamic service.
            var crud = scope.ServiceProvider.GetRequiredService<IDataSurfaceCrudService>();
            var act = () => crud.CreateAsync("Widget", new JsonObject { ["name"] = "Blocked" });

            await act.Should().ThrowAsync<UnauthorizedAccessException>();
        }
    }
}
