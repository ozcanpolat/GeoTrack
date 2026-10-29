using GeoTrack.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Web.Data;


public static class DemoDataSeeder
{
    public static async Task SeedAsync(GeoTrackDbContext db, CancellationToken ct = default)
    {
        if (await db.Incidents.AnyAsync(ct))
        {
            return;
        }

        var now = DateTime.UtcNow;

        Incident Make(string title, string description, IncidentCategory category,
            IncidentStatus status, IncidentPriority priority, double lat, double lng, int hoursAgo) => new()
        {
            Title = "[DEMO] " + title,
            Description = description,
            Category = category,
            Status = status,
            Priority = priority,
            Location = GeoFactory.CreatePoint(lat, lng),
            CreatedAt = now.AddHours(-hoursAgo),
            UpdatedAt = now.AddHours(-hoursAgo)
        };

        db.Incidents.AddRange(
            Make("Yağmur suyu mazgalı tıkalı",
                "Yoğun yağışta yol su altında kalıyor, mazgal temizlenmeli.",
                IncidentCategory.WaterInfrastructure, IncidentStatus.New, IncidentPriority.High, 40.2100, 28.9400, 3),
            Make("Yolda derin çukur",
                "Ana cadde üzerinde araçlara zarar verebilecek derinlikte çukur var.",
                IncidentCategory.Road, IncidentStatus.Assigned, IncidentPriority.Medium, 40.2115, 28.9420, 20),
            Make("Sokak lambası yanmıyor",
                "Sokaktaki iki aydınlatma direği geceleri yanmıyor.",
                IncidentCategory.Electricity, IncidentStatus.InProgress, IncidentPriority.Medium, 40.2090, 28.9385, 30),
            Make("Taşan çöp konteyneri",
                "Konteyner günlerdir boşaltılmadı, çevreye çöp saçılıyor.",
                IncidentCategory.Waste, IncidentStatus.New, IncidentPriority.Low, 40.2125, 28.9440, 6),
            Make("Açık kalmış trafo kapağı",
                "Trafo kutusunun kapağı açık, güvenlik riski oluşturuyor.",
                IncidentCategory.Electricity, IncidentStatus.New, IncidentPriority.Critical, 40.2105, 28.9412, 1),
            Make("Su borusu patlağı",
                "Kaldırımda sürekli su sızıyor; basınç düşüklüğü bildiriliyor.",
                IncidentCategory.WaterInfrastructure, IncidentStatus.Resolved, IncidentPriority.High, 40.2200, 28.9500, 72));

        await db.SaveChangesAsync(ct);
    }
}
