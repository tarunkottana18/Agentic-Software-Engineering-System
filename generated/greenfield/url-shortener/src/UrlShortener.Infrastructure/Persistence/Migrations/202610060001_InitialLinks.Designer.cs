using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UrlShortener.Infrastructure.Persistence;

namespace UrlShortener.Infrastructure.Persistence.Migrations;

[DbContext(typeof(LinkDbContext))]
[Migration("202610060001_InitialLinks")]
partial class InitialLinks
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        modelBuilder.Entity("UrlShortener.Core.Entities.ShortLink", entity =>
        {
            entity.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("INTEGER");
            entity.Property<string>("Code").IsRequired().HasMaxLength(7).UseCollation("BINARY").HasColumnType("TEXT");
            entity.Property<int>("ClickCount").ValueGeneratedOnAdd().HasDefaultValue(0).HasColumnType("INTEGER");
            entity.Property<string>("OriginalUrl").IsRequired().HasMaxLength(2048).UseCollation("BINARY").HasColumnType("TEXT");
            entity.HasKey("Id");
            entity.HasIndex("Code").IsUnique().HasDatabaseName("IX_Links_Code");
            entity.HasIndex("OriginalUrl").IsUnique().HasDatabaseName("IX_Links_OriginalUrl");
            entity.ToTable("Links");
        });
    }
}