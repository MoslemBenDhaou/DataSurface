using Microsoft.EntityFrameworkCore;

namespace DataSurface.Tests.Integration.TestFixtures;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    public DbSet<TestUser> Users => Set<TestUser>();
    public DbSet<TestPost> Posts => Set<TestPost>();
    public DbSet<TestComment> Comments => Set<TestComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(50);
        });

        modelBuilder.Entity<TestPost>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasOne(x => x.Author)
                .WithMany(u => u.Posts)
                .HasForeignKey(x => x.AuthorId);
        });

        modelBuilder.Entity<TestComment>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Post)
                .WithMany(p => p.Comments)
                .HasForeignKey(x => x.PostId);
        });
    }
}

public class TestUser
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? Status { get; set; }
    public string? TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public ICollection<TestPost> Posts { get; set; } = new List<TestPost>();
}

public class TestPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public string? Description { get; set; }
    public int AuthorId { get; set; }
    public string Status { get; set; } = "draft";
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TestUser? Author { get; set; }
    public ICollection<TestComment> Comments { get; set; } = new List<TestComment>();
}

public class TestComment
{
    public int Id { get; set; }
    public string Content { get; set; } = "";
    public int PostId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TestPost? Post { get; set; }
}
