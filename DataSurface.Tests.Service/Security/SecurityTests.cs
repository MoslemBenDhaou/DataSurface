using System.Linq.Expressions;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Services;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.Security;

/// <summary>
/// Tests for the security pipeline: tenant isolation, row-level security,
/// field authorization/redaction, resource authorization, and audit logging.
/// Covers strategy §4.6.
/// </summary>
public class SecurityTests : IDisposable
{
    private readonly CrudTestDbContext _db;

    public SecurityTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private static ResourceContract BuildTenantContract()
    {
        return new ResourceContractBuilder("TenantItem", "tenant-items")
            .Key("Id", FieldType.Int32)
            .Tenant("TenantId", "tenantId", "tenant_id", required: true)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("TenantId").OfType(FieldType.String).InRead().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    private static ResourceContract BuildSimpleContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    // ────────────────────────────────────────────
    //  Tenant Isolation
    // ────────────────────────────────────────────

    [Fact]
    public async Task List_WithTenantIsolation_ReturnsOnlyCurrentTenantRecords()
    {
        _db.TenantItems.AddRange(
            new TenantItem { Name = "T1-A", TenantId = "tenant-a", Price = 10m },
            new TenantItem { Name = "T1-B", TenantId = "tenant-a", Price = 20m },
            new TenantItem { Name = "T2-A", TenantId = "tenant-b", Price = 30m });
        await _db.SaveChangesAsync();

        using var factory = new TestServiceFactory(_db, new[] { BuildTenantContract() }, svc =>
        {
            svc.AddSingleton<ITenantResolver>(new FixedTenantResolver("tenant-a"));
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var result = await factory.CrudService.ListAsync("TenantItem", new QuerySpec());

        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(i =>
            i["tenantId"]!.GetValue<string>().Should().Be("tenant-a"));
    }

    [Fact]
    public async Task Get_WithTenantIsolation_ReturnsNullForOtherTenantEntity()
    {
        _db.TenantItems.Add(new TenantItem { Name = "Other", TenantId = "tenant-b", Price = 5m });
        await _db.SaveChangesAsync();
        var id = _db.TenantItems.First().Id;

        using var factory = new TestServiceFactory(_db, new[] { BuildTenantContract() }, svc =>
        {
            svc.AddSingleton<ITenantResolver>(new FixedTenantResolver("tenant-a"));
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var result = await factory.CrudService.GetAsync("TenantItem", id);

        result.Should().BeNull();
    }

    [Fact]
    public async Task List_RequiredTenantClaimMissing_ThrowsUnauthorized()
    {
        using var factory = new TestServiceFactory(_db, new[] { BuildTenantContract() }, svc =>
        {
            svc.AddSingleton<ITenantResolver>(new FixedTenantResolver(null));
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var act = () => factory.CrudService.ListAsync("TenantItem", new QuerySpec());

        // The UnauthorizedAccessException is thrown inside a reflection call,
        // so it gets wrapped in a TargetInvocationException
        var ex = await act.Should().ThrowAsync<Exception>();
        ex.Which.InnerException.Should().BeOfType<UnauthorizedAccessException>();
    }

    // ────────────────────────────────────────────
    //  Row-Level Security (IResourceFilter)
    // ────────────────────────────────────────────

    [Fact]
    public async Task List_WithResourceFilter_ReturnsOnlyFilteredRecords()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "Active1", Price = 10m, IsActive = true },
            new SimpleItem { Name = "Active2", Price = 20m, IsActive = true },
            new SimpleItem { Name = "Inactive", Price = 30m, IsActive = false });
        await _db.SaveChangesAsync();

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IResourceFilter<SimpleItem>>(new ActiveOnlyFilter());
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var result = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());

        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(i =>
            i["name"]!.GetValue<string>().Should().StartWith("Active"));
    }

    [Fact]
    public async Task Get_WithResourceFilter_ReturnsNullForFilteredOutEntity()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Inactive", Price = 30m, IsActive = false });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IResourceFilter<SimpleItem>>(new ActiveOnlyFilter());
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var result = await factory.CrudService.GetAsync("SimpleItem", id);

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────
    //  Resource Authorization
    // ────────────────────────────────────────────

    [Fact]
    public async Task Get_ResourceAuthorizerDenies_ThrowsUnauthorized()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Secret", Price = 99m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IResourceAuthorizer>(new DenyAllAuthorizer());
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var act = () => factory.CrudService.GetAsync("SimpleItem", id);

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*denied*");
    }

    // ────────────────────────────────────────────
    //  Field Authorization — Read Redaction
    // ────────────────────────────────────────────

    [Fact]
    public async Task Get_FieldAuthorizerRedactsUnauthorizedFields()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "Item", Price = 99.99m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IFieldAuthorizer>(new HidePriceFieldAuthorizer());
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var result = await factory.CrudService.GetAsync("SimpleItem", id);

        result.Should().NotBeNull();
        result!.ContainsKey("name").Should().BeTrue();
        result!.ContainsKey("price").Should().BeFalse("price should be redacted");
    }

    [Fact]
    public async Task List_FieldAuthorizerRedactsFromAllItems()
    {
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "A", Price = 10m },
            new SimpleItem { Name = "B", Price = 20m });
        await _db.SaveChangesAsync();

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IFieldAuthorizer>(new HidePriceFieldAuthorizer());
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var result = await factory.CrudService.ListAsync("SimpleItem", new QuerySpec());

        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(i => i.ContainsKey("price").Should().BeFalse());
    }

    // ────────────────────────────────────────────
    //  Field Write Authorization
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_FieldWriteAuthorizerRejectsUnauthorizedField()
    {
        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IFieldAuthorizer>(new ReadOnlyPriceFieldAuthorizer());
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        var act = () => factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "New", ["price"] = 100m });

        await act.Should().ThrowAsync<UnauthorizedAccessException>()
            .WithMessage("*price*");
    }

    // ────────────────────────────────────────────
    //  Audit Logging
    // ────────────────────────────────────────────

    [Fact]
    public async Task Create_AuditLoggerReceivesCorrectEntry()
    {
        var auditLogger = new RecordingAuditLogger();

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IAuditLogger>(auditLogger);
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "Audited", ["price"] = 5m });

        auditLogger.Entries.Should().ContainSingle();
        auditLogger.Entries[0].Operation.Should().Be(CrudOperation.Create);
        auditLogger.Entries[0].ResourceKey.Should().Be("SimpleItem");
        auditLogger.Entries[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task Get_AuditLoggerReceivesReadEntry()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "AuditRead", Price = 1m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var auditLogger = new RecordingAuditLogger();

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IAuditLogger>(auditLogger);
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        await factory.CrudService.GetAsync("SimpleItem", id);

        auditLogger.Entries.Should().ContainSingle();
        auditLogger.Entries[0].Operation.Should().Be(CrudOperation.Get);
    }

    [Fact]
    public async Task Delete_AuditLoggerReceivesDeleteEntry()
    {
        _db.SimpleItems.Add(new SimpleItem { Name = "AuditDel", Price = 1m });
        await _db.SaveChangesAsync();
        var id = _db.SimpleItems.First().Id;

        var auditLogger = new RecordingAuditLogger();

        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() }, svc =>
        {
            svc.AddSingleton<IAuditLogger>(auditLogger);
            svc.AddSingleton<CrudSecurityDispatcher>();
        });

        await factory.CrudService.DeleteAsync("SimpleItem", id);

        auditLogger.Entries.Should().ContainSingle();
        auditLogger.Entries[0].Operation.Should().Be(CrudOperation.Delete);
    }

    // ────────────────────────────────────────────
    //  No Security → Normal Flow
    // ────────────────────────────────────────────

    [Fact]
    public async Task NoSecurityRegistered_CrudWorksNormally()
    {
        using var factory = new TestServiceFactory(_db, new[] { BuildSimpleContract() });
        // No CrudSecurityDispatcher registered at all

        var result = await factory.CrudService.CreateAsync("SimpleItem",
            new JsonObject { ["name"] = "NoSecurity", ["price"] = 1m });

        result.Should().NotBeNull();
        result["name"]!.GetValue<string>().Should().Be("NoSecurity");
    }

    // ════════════════════════════════════════════
    //  Test Doubles
    // ════════════════════════════════════════════

    private sealed class FixedTenantResolver : ITenantResolver
    {
        private readonly string? _tenantId;
        public FixedTenantResolver(string? tenantId) => _tenantId = tenantId;
        public string? GetTenantId() => _tenantId;
    }

    private sealed class ActiveOnlyFilter : IResourceFilter<SimpleItem>
    {
        public Expression<Func<SimpleItem, bool>>? GetFilter(ResourceContract contract)
            => x => x.IsActive;
    }

    private sealed class DenyAllAuthorizer : IResourceAuthorizer
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ResourceContract contract, object? entity, CrudOperation operation, CancellationToken ct)
            => Task.FromResult(AuthorizationResult.Fail("Access denied."));
    }

    private sealed class HidePriceFieldAuthorizer : IFieldAuthorizer
    {
        public bool CanReadField(ResourceContract contract, string fieldName)
            => !fieldName.Equals("price", StringComparison.OrdinalIgnoreCase);
        public bool CanWriteField(ResourceContract contract, string fieldName, CrudOperation operation)
            => true;
    }

    private sealed class ReadOnlyPriceFieldAuthorizer : IFieldAuthorizer
    {
        public bool CanReadField(ResourceContract contract, string fieldName) => true;
        public bool CanWriteField(ResourceContract contract, string fieldName, CrudOperation operation)
            => !fieldName.Equals("price", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<AuditLogEntry> Entries { get; } = new();
        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
