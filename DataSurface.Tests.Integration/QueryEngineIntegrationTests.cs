using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore;
using DataSurface.EFCore.Contracts;
using DataSurface.Tests.Integration.TestFixtures;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataSurface.Tests.Integration;

public class QueryEngineIntegrationTests : IDisposable
{
    private readonly TestDbContext _db;
    private readonly EfCrudQueryEngine _engine;

    public QueryEngineIntegrationTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _db = new TestDbContext(options);
        _engine = new EfCrudQueryEngine();

        SeedData();
    }

    private void SeedData()
    {
        var users = new[]
        {
            new TestUser { Id = 1, Name = "Alice Johnson", Email = "alice@example.com", Status = "active", IsActive = true },
            new TestUser { Id = 2, Name = "Bob Smith", Email = "bob@example.com", Status = "inactive", IsActive = false },
            new TestUser { Id = 3, Name = "Charlie Brown", Email = "charlie@example.com", Status = "active", IsActive = true },
            new TestUser { Id = 4, Name = "Diana Prince", Email = null, Status = "pending", IsActive = true },
            new TestUser { Id = 5, Name = "Eve Wilson", Email = "eve@example.com", Status = "active", IsActive = true }
        };

        var posts = new[]
        {
            new TestPost { Id = 1, Title = "Hello World", Content = "First post content", Description = "A greeting", AuthorId = 1, Status = "published", Price = 10.99m },
            new TestPost { Id = 2, Title = "Goodbye World", Content = "Farewell content", Description = "A farewell with World", AuthorId = 1, Status = "draft", Price = 5.99m },
            new TestPost { Id = 3, Title = "Tech Article", Content = "Technical content", Description = "Something technical", AuthorId = 2, Status = "published", Price = 15.99m },
            new TestPost { Id = 4, Title = "News Update", Content = "News content", Description = "Breaking news", AuthorId = 3, Status = "archived", Price = 0m },
            new TestPost { Id = 5, Title = "Tutorial", Content = "Tutorial content about World", Description = "How to do things", AuthorId = 1, Status = "published", Price = 25.99m }
        };

        _db.Users.AddRange(users);
        _db.Posts.AddRange(posts);
        _db.SaveChanges();
    }

    private ResourceContract CreateUserContract()
    {
        var fields = new List<FieldContract>
        {
            CreateField("Id", "id", FieldType.Int32, filterable: true, sortable: true),
            CreateField("Name", "name", FieldType.String, filterable: true, sortable: true, searchable: true),
            CreateField("Email", "email", FieldType.String, filterable: true, searchable: true, nullable: true),
            CreateField("Status", "status", FieldType.String, filterable: true),
            CreateField("IsActive", "isActive", FieldType.Boolean, filterable: true)
        };

        return new ResourceContract(
            ResourceKey: "TestUser",
            Route: "test-users",
            Backend: StorageBackend.EfCore,
            Key: new ResourceKeyContract("Id", FieldType.Int32),
            Query: new QueryContract(100, 
                fields.Where(f => f.Filterable).Select(f => f.ApiName).ToList(),
                fields.Where(f => f.Sortable).Select(f => f.ApiName).ToList(),
                fields.Where(f => f.Searchable).Select(f => f.ApiName).ToList(),
                null),
            Read: new ReadContract(Array.Empty<string>(), 1, Array.Empty<string>()),
            Fields: fields,
            Relations: Array.Empty<RelationContract>(),
            Operations: new Dictionary<CrudOperation, OperationContract>(),
            Security: new SecurityContract(new Dictionary<CrudOperation, string?>())
        );
    }

    private ResourceContract CreatePostContract()
    {
        var fields = new List<FieldContract>
        {
            CreateField("Id", "id", FieldType.Int32, filterable: true, sortable: true),
            CreateField("Title", "title", FieldType.String, filterable: true, sortable: true, searchable: true),
            CreateField("Content", "content", FieldType.String, searchable: true),
            CreateField("Description", "description", FieldType.String, searchable: true),
            CreateField("Status", "status", FieldType.String, filterable: true),
            CreateField("Price", "price", FieldType.Decimal, filterable: true, sortable: true)
        };

        return new ResourceContract(
            ResourceKey: "TestPost",
            Route: "test-posts",
            Backend: StorageBackend.EfCore,
            Key: new ResourceKeyContract("Id", FieldType.Int32),
            Query: new QueryContract(50,
                fields.Where(f => f.Filterable).Select(f => f.ApiName).ToList(),
                fields.Where(f => f.Sortable).Select(f => f.ApiName).ToList(),
                fields.Where(f => f.Searchable).Select(f => f.ApiName).ToList(),
                null),
            Read: new ReadContract(Array.Empty<string>(), 1, Array.Empty<string>()),
            Fields: fields,
            Relations: Array.Empty<RelationContract>(),
            Operations: new Dictionary<CrudOperation, OperationContract>(),
            Security: new SecurityContract(new Dictionary<CrudOperation, string?>())
        );
    }

    private static FieldContract CreateField(string name, string apiName, FieldType type,
        bool filterable = false, bool sortable = false, bool searchable = false, bool nullable = false)
    {
        return new FieldContract(name, apiName, type, nullable, true, true, true,
            filterable, sortable, false, false, searchable, false, null, null,
            new FieldValidationContract(false, null, null, null, null, null));
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public void FullTextSearch_FindsMatchesInTitle()
    {
        var contract = CreatePostContract();
        var spec = new QuerySpec(Search: "Hello");

        var result = _engine.Apply(_db.Posts, contract, spec).ToList();

        result.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Should().Contain(p => p.Title == "Hello World");
    }

    [Fact]
    public void FullTextSearch_FindsMatchesInContent()
    {
        var contract = CreatePostContract();
        var spec = new QuerySpec(Search: "Tutorial");

        var result = _engine.Apply(_db.Posts, contract, spec).ToList();

        result.Should().HaveCount(1);
        result.First().Title.Should().Be("Tutorial");
    }

    [Fact]
    public void FullTextSearch_FindsMatchesAcrossMultipleFields()
    {
        var contract = CreatePostContract();
        var spec = new QuerySpec(Search: "content");

        var result = _engine.Apply(_db.Posts, contract, spec).ToList();

        result.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void IsNullFilter_FindsNullEmails()
    {
        var contract = CreateUserContract();
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["email"] = "isnull:true" });

        var result = _engine.Apply(_db.Users, contract, spec).ToList();

        result.Should().HaveCount(1);
        result.First().Name.Should().Be("Diana Prince");
    }

    [Fact]
    public void IsNullFilter_FindsNonNullEmails()
    {
        var contract = CreateUserContract();
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["email"] = "isnull:false" });

        var result = _engine.Apply(_db.Users, contract, spec).ToList();

        result.Should().HaveCount(4);
        result.Should().OnlyContain(u => u.Email != null);
    }

    [Fact]
    public void InFilter_FindsMultipleStatuses()
    {
        var contract = CreateUserContract();
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["status"] = "in:active|pending" });

        var result = _engine.Apply(_db.Users, contract, spec).ToList();

        result.Should().HaveCount(4);
        result.Should().OnlyContain(u => u.Status == "active" || u.Status == "pending");
    }

    [Fact]
    public void PriceFilter_GreaterThan()
    {
        var contract = CreatePostContract();
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["price"] = "gt:10" });

        var result = _engine.Apply(_db.Posts, contract, spec).ToList();

        result.Should().HaveCount(3);
        result.Should().OnlyContain(p => p.Price > 10);
    }

    [Fact]
    public void MultipleFilters_AppliedTogether()
    {
        var contract = CreatePostContract();
        var spec = new QuerySpec(Filters: new Dictionary<string, string>
        {
            ["status"] = "published",
            ["price"] = "gte:10"
        });

        var result = _engine.Apply(_db.Posts, contract, spec).ToList();

        result.Should().HaveCountGreaterThanOrEqualTo(2);
        result.Should().OnlyContain(p => p.Status == "published" && p.Price >= 10);
    }

    [Fact]
    public void Sort_ByPriceDescending()
    {
        var contract = CreatePostContract();
        var spec = new QuerySpec(Sort: "-price");

        var result = _engine.Apply(_db.Posts, contract, spec).ToList();

        result.First().Price.Should().Be(25.99m);
        result.Last().Price.Should().Be(0m);
    }

    [Fact]
    public void Pagination_SecondPage()
    {
        var contract = CreateUserContract();
        var spec = new QuerySpec(Page: 2, PageSize: 2);

        var result = _engine.Apply(_db.Users, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.First().Id.Should().Be(3);
    }

    [Fact]
    public void CombinedSearchAndFilter()
    {
        var contract = CreatePostContract();
        var spec = new QuerySpec(
            Search: "Hello",
            Filters: new Dictionary<string, string> { ["status"] = "published" }
        );

        var result = _engine.Apply(_db.Posts, contract, spec).ToList();

        result.Should().HaveCountGreaterThanOrEqualTo(1);
        result.Should().Contain(p => p.Title == "Hello World");
    }

    [Fact]
    public void BooleanFilter_FindsActiveUsers()
    {
        var contract = CreateUserContract();
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["isActive"] = "true" });

        var result = _engine.Apply(_db.Users, contract, spec).ToList();

        result.Should().HaveCount(4);
        result.Should().OnlyContain(u => u.IsActive);
    }

    [Fact]
    public void ContainsFilter_FindsMatchingNames()
    {
        var contract = CreateUserContract();
        var spec = new QuerySpec(Filters: new Dictionary<string, string> { ["name"] = "contains:son" });

        var result = _engine.Apply(_db.Users, contract, spec).ToList();

        result.Should().HaveCount(2);
        result.Should().Contain(u => u.Name == "Alice Johnson");
        result.Should().Contain(u => u.Name == "Eve Wilson");
    }
}
