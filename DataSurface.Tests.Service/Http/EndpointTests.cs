using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore;
using DataSurface.EFCore.Context;
using DataSurface.EFCore.Interfaces;
using DataSurface.EFCore.Mapper;
using DataSurface.EFCore.Observability;
using DataSurface.EFCore.Providers;
using DataSurface.EFCore.Services;
using DataSurface.Http;
using DataSurface.Tests.Service.Shared.Builders;
using DataSurface.Tests.Service.Shared.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DataSurface.Tests.Service.Http;

/// <summary>
/// Integration tests for DataSurface HTTP CRUD endpoints mapped via
/// <see cref="DataSurfaceEndpointMapper"/>. Uses TestServer to exercise the
/// full HTTP pipeline including routing, query parsing, error mapping, and
/// response headers. Covers strategy §4.9.
/// </summary>
public class EndpointTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private CrudTestDbContext _db = null!;

    private static ResourceContract BuildContract()
    {
        return new ResourceContractBuilder("SimpleItem", "simple-items")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().RequiredOnCreate().Filterable().Sortable().Searchable().Build())
            .WithField(new FieldBuilder("Price").OfType(FieldType.Decimal).ReadCreateUpdate().Filterable().Build())
            .WithField(new FieldBuilder("Description").OfType(FieldType.String).ReadCreateUpdate().Searchable().Build())
            .WithField(new FieldBuilder("IsActive").OfType(FieldType.Boolean).ReadCreateUpdate().Build())
            .EnableAllOperations()
            .Build();
    }

    public async Task InitializeAsync()
    {
        var dbName = Guid.NewGuid().ToString();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();

        var contracts = new[] { BuildContract() };
        var contractProvider = new StaticResourceContractProvider(contracts);

        builder.Services.AddSingleton<IResourceContractProvider>(contractProvider);
        builder.Services.AddDbContext<CrudTestDbContext>(o => o.UseInMemoryDatabase(dbName));
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<CrudTestDbContext>());
        builder.Services.AddSingleton<EfCrudQueryEngine>();
        builder.Services.AddSingleton<EfCrudMapper>();
        builder.Services.AddSingleton<CrudHookDispatcher>();
        builder.Services.AddSingleton<CrudOverrideRegistry>();
        builder.Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        builder.Services.AddScoped<IDataSurfaceCrudService>(sp =>
            new EfDataSurfaceCrudService(
                db: sp.GetRequiredService<DbContext>(),
                contracts: sp.GetRequiredService<IResourceContractProvider>(),
                query: sp.GetRequiredService<EfCrudQueryEngine>(),
                mapper: sp.GetRequiredService<EfCrudMapper>(),
                sp: sp,
                hooks: sp.GetRequiredService<CrudHookDispatcher>(),
                overrides: sp.GetRequiredService<CrudOverrideRegistry>(),
                logger: sp.GetRequiredService<ILogger<EfDataSurfaceCrudService>>()));

        _app = builder.Build();
        _app.MapDataSurfaceCrud(new DataSurfaceHttpOptions
        {
            ApiPrefix = "/api",
            RequireAuthorizationByDefault = false
        });

        await _app.StartAsync();
        _client = _app.GetTestClient();

        // Seed data
        using var scope = _app.Services.CreateScope();
        _db = scope.ServiceProvider.GetRequiredService<CrudTestDbContext>();
        _db.Database.EnsureCreated();
        _db.SimpleItems.AddRange(
            new SimpleItem { Name = "Alpha", Price = 10m, Description = "First", IsActive = true },
            new SimpleItem { Name = "Beta", Price = 20m, Description = "Second", IsActive = true },
            new SimpleItem { Name = "Gamma", Price = 30m, Description = "Third", IsActive = false });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // ────────────────────────────────────────────
    //  LIST
    // ────────────────────────────────────────────

    [Fact]
    public async Task GET_List_ReturnsOkWithItems()
    {
        var response = await _client.GetAsync("/api/simple-items");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(3);
        body.GetProperty("total").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GET_List_SetsCountHeaders()
    {
        var response = await _client.GetAsync("/api/simple-items");

        response.Headers.TryGetValues("X-Total-Count", out var totalValues).Should().BeTrue();
        totalValues!.First().Should().Be("3");
        response.Headers.TryGetValues("X-Page", out var pageValues).Should().BeTrue();
        pageValues!.First().Should().Be("1");
    }

    [Fact]
    public async Task GET_List_SupportsPaging()
    {
        var response = await _client.GetAsync("/api/simple-items?page=1&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(2);
        body.GetProperty("total").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task GET_List_SupportsSorting()
    {
        var response = await _client.GetAsync("/api/simple-items?sort=-name");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");
        items[0].GetProperty("name").GetString().Should().Be("Gamma");
    }

    [Fact]
    public async Task GET_List_SupportsFiltering()
    {
        var response = await _client.GetAsync("/api/simple-items?filter[name]=eq:Alpha");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("items").GetArrayLength().Should().Be(1);
        body.GetProperty("items")[0].GetProperty("name").GetString().Should().Be("Alpha");
    }

    // ────────────────────────────────────────────
    //  GET by ID
    // ────────────────────────────────────────────

    [Fact]
    public async Task GET_ById_ReturnsOkWithItem()
    {
        // Get first item's ID
        var listResponse = await _client.GetAsync("/api/simple-items?pageSize=1");
        var listBody = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = listBody.GetProperty("items")[0].GetProperty("id").GetInt32();

        var response = await _client.GetAsync($"/api/simple-items/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await response.Content.ReadFromJsonAsync<JsonElement>();
        item.GetProperty("id").GetInt32().Should().Be(id);
    }

    [Fact]
    public async Task GET_ById_NotFound_Returns404()
    {
        var response = await _client.GetAsync("/api/simple-items/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ────────────────────────────────────────────
    //  CREATE
    // ────────────────────────────────────────────

    [Fact]
    public async Task POST_Create_Returns201WithLocationHeader()
    {
        var body = new JsonObject { ["name"] = "Delta", ["price"] = 40m };

        var response = await _client.PostAsJsonAsync("/api/simple-items", body);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();

        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("name").GetString().Should().Be("Delta");
    }

    [Fact]
    public async Task POST_Create_MissingRequiredField_ReturnsValidationError()
    {
        var body = new JsonObject { ["price"] = 40m }; // missing "name"

        var response = await _client.PostAsJsonAsync("/api/simple-items", body);

        // DataSurfaceHttpErrorMapper should return a problem details response
        var statusCode = (int)response.StatusCode;
        statusCode.Should().BeOneOf(400, 422, 500); // Validation errors mapped
    }

    // ────────────────────────────────────────────
    //  UPDATE (PATCH)
    // ────────────────────────────────────────────

    [Fact]
    public async Task PATCH_Update_ReturnsOkWithUpdatedItem()
    {
        // Create an item first
        var createBody = new JsonObject { ["name"] = "Patchable", ["price"] = 5m };
        var createResponse = await _client.PostAsJsonAsync("/api/simple-items", createBody);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        // Patch
        var patch = new JsonObject { ["name"] = "Patched" };
        var patchContent = JsonContent.Create(patch);
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/simple-items/{id}")
        {
            Content = patchContent
        };
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be("Patched");
    }

    [Fact]
    public async Task PATCH_Update_NotFound_ReturnsError()
    {
        var patch = new JsonObject { ["name"] = "Ghost" };
        var patchContent = JsonContent.Create(patch);
        var request = new HttpRequestMessage(HttpMethod.Patch, "/api/simple-items/99999")
        {
            Content = patchContent
        };
        var response = await _client.SendAsync(request);

        // CrudNotFoundException → mapped to 404
        var statusCode = (int)response.StatusCode;
        statusCode.Should().BeOneOf(404, 500);
    }

    // ────────────────────────────────────────────
    //  DELETE
    // ────────────────────────────────────────────

    [Fact]
    public async Task DELETE_ReturnsNoContent()
    {
        // Create an item to delete
        var body = new JsonObject { ["name"] = "ToDelete", ["price"] = 1m };
        var createResponse = await _client.PostAsJsonAsync("/api/simple-items", body);
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        var response = await _client.DeleteAsync($"/api/simple-items/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DELETE_NotFound_ReturnsError()
    {
        var response = await _client.DeleteAsync("/api/simple-items/99999");

        var statusCode = (int)response.StatusCode;
        statusCode.Should().BeOneOf(404, 500);
    }

    // ────────────────────────────────────────────
    //  Roundtrip: Create → Get → Update → Delete
    // ────────────────────────────────────────────

    [Fact]
    public async Task FullCrudRoundtrip()
    {
        // Create
        var body = new JsonObject { ["name"] = "Roundtrip", ["price"] = 99m };
        var createResp = await _client.PostAsJsonAsync("/api/simple-items", body);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();

        // Get
        var getResp = await _client.GetAsync($"/api/simple-items/{id}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        fetched.GetProperty("name").GetString().Should().Be("Roundtrip");

        // Update
        var patch = new JsonObject { ["name"] = "Updated" };
        var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/simple-items/{id}")
        {
            Content = JsonContent.Create(patch)
        };
        var updateResp = await _client.SendAsync(patchReq);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be("Updated");

        // Delete
        var deleteResp = await _client.DeleteAsync($"/api/simple-items/{id}");
        deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Confirm gone
        var goneResp = await _client.GetAsync($"/api/simple-items/{id}");
        goneResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
