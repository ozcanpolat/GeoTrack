using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Web.Data;

public class GeoTrackDbContext(DbContextOptions<GeoTrackDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<UserNotification> Notifications => Set<UserNotification>();
    public DbSet<SearchHistory> SearchHistory => Set<SearchHistory>();
    public DbSet<IncidentHistory> IncidentHistory => Set<IncidentHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserNotification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Message).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SearchHistory>(e =>
        {
            e.ToTable("search_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Query).HasMaxLength(300).IsRequired();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        modelBuilder.Entity<IncidentHistory>(e =>
        {
            e.ToTable("incident_history");
            e.HasKey(x => x.Id).HasName("PK_incident_history");
            e.Property(x => x.Action).HasMaxLength(48).IsRequired();
            e.Property(x => x.PreviousValue).HasMaxLength(64);
            e.Property(x => x.NewValue).HasMaxLength(64);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => new { x.IncidentId, x.CreatedAtUtc });
            e.HasOne<Incident>().WithMany().HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.Id).HasName("PK_audit_logs");
            e.Property(x => x.ActorUserId).HasMaxLength(450);
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(64).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(4000);
            e.HasIndex(x => x.TimestampUtc);
        });

        modelBuilder.Entity<Incident>(e =>
        {
            e.ToTable("incidents");
            e.HasKey(x => x.Id);

            e.Property(x => x.Title).IsRequired().HasMaxLength(150);
            e.Property(x => x.UserId).HasColumnName("CreatedByUserId").HasColumnType("text");
            e.HasIndex(x => x.UserId).HasDatabaseName("IX_incidents_CreatedByUserId");
            e.Property(x => x.AssignedToUserId).HasColumnType("text");
            e.HasIndex(x => x.AssignedToUserId);
            e.Property(x => x.Description).IsRequired().HasMaxLength(2000);

            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(32).IsRequired();

            e.Property(x => x.CreatedAt).IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.Property(x => x.Location)
                .HasColumnType("geography (Point, 4326)")
                .IsRequired();

            e.HasIndex(x => x.Location).HasMethod("gist");

            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Category);
        });
    }
}
