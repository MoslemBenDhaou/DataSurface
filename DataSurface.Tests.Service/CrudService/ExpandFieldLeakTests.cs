using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;
using DataSurface.EFCore.Contracts;
using DataSurface.Tests.Service.Shared;
using DataSurface.Tests.Service.Shared.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DataSurface.Tests.Service.CrudService;

/// <summary>
/// Regression test for B3: expanding a relation must serialize the related entity through
/// the related resource's OWN contract, so fields that are not part of that resource's read
/// shape (hidden / non-read / field-authorized) are never leaked via <c>expand</c>.
/// </summary>
public class ExpandFieldLeakTests : IDisposable
{
    private readonly ExpandTestDbContext _db;
    private readonly TestServiceFactory _factory;

    public ExpandFieldLeakTests()
    {
        var options = new DbContextOptionsBuilder<ExpandTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new ExpandTestDbContext(options);
        _db.Database.EnsureCreated();

        // The Author resource exposes only Id + Name. "Secret" exists on the entity but is
        // NOT part of the read contract — it must never appear in an expanded payload.
        var author = new ResourceContractBuilder("ExpandAuthor", "expand-authors")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).InRead().Build())
            .EnableAllOperations()
            .Build();

        var authorRelation = new RelationContract(
            Name: "Author",
            ApiName: "author",
            Kind: RelationKind.ManyToOne,
            TargetResourceKey: "ExpandAuthor",
            Read: new RelationReadContract(ExpandAllowed: true, DefaultExpanded: false),
            Write: new RelationWriteContract(RelationWriteMode.None, null, false, null));

        var post = new ResourceContractBuilder("ExpandPost", "expand-posts")
            .Key("Id", FieldType.Int32)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Build())
            .WithField(new FieldBuilder("Title").OfType(FieldType.String).InRead().Build())
            .WithRelation(authorRelation)
            .WithExpandAllowed("author")
            .EnableAllOperations()
            .Build();

        _factory = new TestServiceFactory(_db, new[] { post, author });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Expand_SerializesRelatedEntity_ViaItsOwnContract_WithoutLeakingHiddenFields()
    {
        _db.ExpandAuthors.Add(new ExpandAuthor { Id = 1, Name = "Ada", Secret = "ssn-123-45-6789" });
        _db.ExpandPosts.Add(new ExpandPost { Id = 1, Title = "Hello", AuthorId = 1 });
        await _db.SaveChangesAsync();

        var result = await _factory.CrudService.GetAsync("ExpandPost", 1, new ExpandSpec(new[] { "author" }));

        result.Should().NotBeNull();
        result!["author"].Should().NotBeNull();

        var authorJson = result["author"]!.AsObject();
        authorJson["name"]!.GetValue<string>().Should().Be("Ada");

        // The bug: SimpleObjectToJson dumped every scalar property of the related entity,
        // bypassing the Author contract. The Secret value must not be present.
        authorJson.Should().NotContainKey("secret");
        authorJson.Should().NotContainKey("Secret");
    }

    private sealed class ExpandTestDbContext : DbContext
    {
        public ExpandTestDbContext(DbContextOptions<ExpandTestDbContext> options) : base(options) { }

        public DbSet<ExpandAuthor> ExpandAuthors => Set<ExpandAuthor>();
        public DbSet<ExpandPost> ExpandPosts => Set<ExpandPost>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExpandAuthor>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
            });
            modelBuilder.Entity<ExpandPost>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();
                e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId);
            });
        }
    }
}

/// <summary>Related entity whose contract intentionally omits <c>Secret</c>.</summary>
public class ExpandAuthor
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Secret { get; set; } = "";
}

/// <summary>Root entity with a many-to-one relation to <see cref="ExpandAuthor"/>.</summary>
public class ExpandPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int AuthorId { get; set; }
    public ExpandAuthor? Author { get; set; }
}
