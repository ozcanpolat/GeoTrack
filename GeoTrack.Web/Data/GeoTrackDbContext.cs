using GeoTrack.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Web.Data;

public class GeoTrackDbContext(DbContextOptions<GeoTrackDbContext> options) : DbContext(options)
{
    public DbSet<Incident> Incidents => Set<Incident>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<Incident>(e =>
        {
            e.ToTable("incidents");
            e.HasKey(x => x.Id);

            e.Property(x => x.Title).IsRequired().HasMaxLength(150);
            e.Property(x => x.Description).IsRequired().HasMaxLength(2000);

            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32).IsRequired();

            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            // geography: mesafeler metre cinsindendir (ST_DWithin / ST_Distance).
            e.Property(x => x.Location)
                .HasColumnType("geography (Point, 4326)")
                .IsRequired();

            // Yakınlık sorguları için GiST mekânsal indeks.
            e.HasIndex(x => x.Location).HasMethod("gist");

            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Category);
        });
    }
}
