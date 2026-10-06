using Microsoft.EntityFrameworkCore;
using UrlShortener.Core.Entities;

namespace UrlShortener.Infrastructure.Persistence
{
    public class UrlDbContext : DbContext
    {
        public UrlDbContext(DbContextOptions<UrlDbContext> options) : base(options) { }

        public DbSet<UrlMapping> UrlMappings { get; set; } = null!;
        public DbSet<EngineeringWorkflowRun> EngineeringWorkflowRuns { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UrlMapping>()
                .HasIndex(u => u.ShortCode)
                .IsUnique();
            
            modelBuilder.Entity<UrlMapping>()
                .HasIndex(u => u.OriginalUrl)
                .IsUnique(false); // Allow multiple short codes for same URL if needed, or set to true

            modelBuilder.Entity<EngineeringWorkflowRun>()
                .HasIndex(workflow => workflow.Status);

            modelBuilder.Entity<EngineeringWorkflowRun>()
                .Property(workflow => workflow.Revision)
                .IsConcurrencyToken();
        }
    }
}
