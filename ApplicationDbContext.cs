using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Article> Articles => Set<Article>();
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Article>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).HasMaxLength(64);
            entity.Property(a => a.Title).IsRequired().HasMaxLength(512);
            entity.Property(a => a.Content).IsRequired();
            entity.Property(a => a.Description).HasMaxLength(2048);
            entity.Property(a => a.Tags).HasMaxLength(1024);
            entity.Property(a => a.AuthorId).IsRequired();
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(a => a.AccessLevel).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(a => a.AuthorId);
            entity.HasIndex(a => a.Status);
            entity.HasIndex(a => new { a.Status, a.PublishedAt });
        });

        builder.Entity<OutboxEvent>(entity =>
        {
            entity.HasKey(o => o.Id);
            entity.Property(o => o.EventType).IsRequired().HasMaxLength(64);
            entity.Property(o => o.AggregateId).IsRequired().HasMaxLength(64);
            entity.Property(o => o.Payload).IsRequired();
            entity.HasIndex(o => o.PublishedAt);
        });
    }
}
