using System.Text.Json.Nodes;
using DataSurface.Core;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.Core.Webhooks;
using DataSurface.EFCore.Exceptions;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// Feature flags are an AND-gate kill-switch: a feature runs only when its flag is enabled AND its wiring
/// is present. Defaults are all-on, so registering the dependency is enough; setting a flag to false turns
/// the feature off even when fully wired. These tests assert the "off even when wired" half (and the
/// matching "on by default when wired" half), across a pipeline flag, a mapper flag, and a service flag.
/// </summary>
public class FeatureFlagsTests : IDisposable
{
    private readonly CrudTestDbContext _db;

    public FeatureFlagsTests()
    {
        var options = new DbContextOptionsBuilder<CrudTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new CrudTestDbContext(options);
        _db.Database.EnsureCreated();
    }

    public void Dispose() => _db.Dispose();

    private sealed class SpyWebhookPublisher : IWebhookPublisher
    {
        public int Calls;
        public Task PublishAsync(WebhookEvent @event, CancellationToken ct = default)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    // Name has a MaxLength(3) constraint; Price declares a DefaultValue — both fully "wired" via the contract.
    private static ResourceContract Contract()
        => new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().MaxLength(3).Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().DefaultValue(9.99m).Build())
            .EnableAllOperations()
            .Build();

    [Fact]
    public async Task EnableFieldValidation_False_SkipsConstraints_EvenWhenContractDeclaresThem()
    {
        using var factory = new TestServiceFactory(_db, new[] { Contract() },
            svc => svc.AddSingleton(new DataSurfaceFeatures { EnableFieldValidation = false }));

        // "toolong" violates MaxLength(3) — but with validation killed, it is accepted.
        var act = () => factory.CrudService.CreateAsync("SimpleItem", new JsonObject { ["name"] = "toolong" });

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnableFieldValidation_DefaultOn_EnforcesConstraints()
    {
        using var factory = new TestServiceFactory(_db, new[] { Contract() }); // no Features registered -> all-on

        var act = () => factory.CrudService.CreateAsync("SimpleItem", new JsonObject { ["name"] = "toolong" });

        await act.Should().ThrowAsync<CrudRequestValidationException>();
    }

    [Fact]
    public async Task EnableDefaultValues_False_DoesNotApplyDefaults_EvenWhenContractDeclaresThem()
    {
        using var factory = new TestServiceFactory(_db, new[] { Contract() },
            svc => svc.AddSingleton(new DataSurfaceFeatures { EnableDefaultValues = false }));

        var created = await factory.CrudService.CreateAsync("SimpleItem", new JsonObject { ["name"] = "ok" });

        created["price"]!.GetValue<decimal>().Should().Be(0m, "DefaultValue(9.99) is not applied when defaults are killed");
    }

    [Fact]
    public async Task EnableDefaultValues_DefaultOn_AppliesDefaults()
    {
        using var factory = new TestServiceFactory(_db, new[] { Contract() });

        var created = await factory.CrudService.CreateAsync("SimpleItem", new JsonObject { ["name"] = "ok" });

        created["price"]!.GetValue<decimal>().Should().Be(9.99m);
    }

    [Fact]
    public async Task EnableWebhooks_False_DoesNotPublish_EvenWhenPublisherRegistered()
    {
        var spy = new SpyWebhookPublisher();
        using var factory = new TestServiceFactory(_db, new[] { Contract() }, svc =>
        {
            svc.AddSingleton<IWebhookPublisher>(spy);
            svc.AddSingleton(new DataSurfaceFeatures { EnableWebhooks = false });
        });

        await factory.CrudService.CreateAsync("SimpleItem", new JsonObject { ["name"] = "ok" });

        spy.Calls.Should().Be(0, "the publisher is wired but the flag kills webhooks");
    }

    [Fact]
    public async Task EnableWebhooks_DefaultOn_PublishesWhenPublisherRegistered()
    {
        var spy = new SpyWebhookPublisher();
        using var factory = new TestServiceFactory(_db, new[] { Contract() },
            svc => svc.AddSingleton<IWebhookPublisher>(spy)); // no Features -> all-on

        await factory.CrudService.CreateAsync("SimpleItem", new JsonObject { ["name"] = "ok" });

        spy.Calls.Should().BeGreaterThan(0, "wired + flag-on (default) = webhooks fire");
    }
}
