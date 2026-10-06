using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Entities;

namespace UrlShortener.Infrastructure.Persistence;

public sealed class LinkDbContext(DbContextOptions<LinkDbContext> options) : DbContext(options)
{
    public DbSet<ShortLink> Links => Set<ShortLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var link = modelBuilder.Entity<ShortLink>();
        link.ToTable("Links");
        link.HasKey(item => item.Id);
        link.Property(item => item.Id).ValueGeneratedOnAdd();
        link.Property(item => item.Code).HasMaxLength(7).IsRequired().UseCollation("BINARY");
        link.Property(item => item.OriginalUrl).HasMaxLength(2048).IsRequired().UseCollation("BINARY");
        link.Property(item => item.ClickCount).IsRequired().HasDefaultValue(0);
        link.HasIndex(item => item.Code).IsUnique().HasDatabaseName("IX_Links_Code");
        link.HasIndex(item => item.OriginalUrl).IsUnique().HasDatabaseName("IX_Links_OriginalUrl");
    }
}