using GeoTrack.Web.Data;
using GeoTrack.Web.DTOs;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Tests;

/// <summary>
/// Gerçek PostgreSQL/PostGIS gerektirir. GEOTRACK_TEST_CONNECTION ortam değişkeni yoksa test "atlandı"
/// olarak raporlanır (sahte/bellek içi veritabanıyla geçmiş gibi gösterilmez).
/// Önce migration uygulanmış olmalıdır: dotnet ef database update (GeoTrack.Web klasöründe).
/// </summary>
public sealed class RequiresPostgisFactAttribute : FactAttribute
{
    public const string EnvVar = "GEOTRACK_TEST_CONNECTION";

    public RequiresPostgisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvVar)))
        {
            Skip = $"{EnvVar} ayarlı değil; gerçek PostGIS testi atlandı.";
        }
    }
}

public class PostgisTests
{
    private static GeoTrackDbContext CreateContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GeoTrackDbContext>()
            .UseNpgsql(connectionString, o => o.UseNetTopologySuite())
            .Options);

    private static GeoTrackDbContext CreateRealContext() =>
        CreateContext(Environment.GetEnvironmentVariable(RequiresPostgisFactAttribute.EnvVar)!);

    private static IncidentInput Input(string title, double lat, double lng) => new()
    {
        Title = title,
        Description = "Test amaçlı geçici kayıt, test sonunda silinir.",
        Category = IncidentCategory.Other,
        Latitude = lat,
        Longitude = lng
    };

    /// <summary>
    /// Bağlantı gerektirmez: yalnızca LINQ ifadesinin SQL'de ST_DWithin'e çevrildiğini doğrular.
    /// Bu bir PostGIS çalışma testi DEĞİLDİR; çalışma testi aşağıdaki [RequiresPostgisFact] testleridir.
    /// </summary>
    [Fact]
    public void NearbyQuery_IsTranslatedToStDWithin()
    {
        using var db = CreateContext("Host=localhost;Database=unused;Username=unused;Password=unused");
        var center = GeoFactory.CreatePoint(40.2, 28.9);

        var sql = db.Incidents
            .Where(i => i.Location.IsWithinDistance(center, 500))
            .ToQueryString();

        Assert.Contains("ST_DWithin", sql);
    }

    [RequiresPostgisFact]
    public async Task Nearby_ReturnsOnlyIncidentsWithinRadius_OrderedByDistance()
    {
        await using var db = CreateRealContext();
        var service = new IncidentService(db);
        var tag = "[TEST-" + Guid.NewGuid().ToString("N")[..8] + "]";
        var ids = new List<int>();

        try
        {
            // Gine Körfezi civarı (0,0): gerçek verilerle çakışmaz. 0.0009° enlem ≈ 100 m.
            ids.Add((await service.CreateAsync(Input(tag + " A", 0.0, 0.0))).Id);
            ids.Add((await service.CreateAsync(Input(tag + " B", 0.0009, 0.0))).Id);   // ~100 m
            ids.Add((await service.CreateAsync(Input(tag + " C", 0.009, 0.0))).Id);    // ~1000 m

            var nearby = await service.NearbyAsync(0.0, 0.0, 500);

            var mine = nearby.Where(x => x.Title.StartsWith(tag)).ToList();
            Assert.Equal(new[] { tag + " A", tag + " B" }, mine.Select(x => x.Title).ToArray());

            var b = mine.Single(x => x.Title == tag + " B");
            Assert.InRange(b.DistanceMeters!.Value, 95, 105);
        }
        finally
        {
            foreach (var id in ids) await service.DeleteAsync(id);
        }
    }

    [RequiresPostgisFact]
    public async Task Crud_RoundTrip_PersistsAndReturnsNullForUnknownId()
    {
        await using var db = CreateRealContext();
        var service = new IncidentService(db);
        var tag = "[TEST-" + Guid.NewGuid().ToString("N")[..8] + "]";
        int? id = null;

        try
        {
            var created = await service.CreateAsync(Input(tag, 0.5, 0.5));
            id = created.Id;
            Assert.Equal(0.5, created.Latitude, 6);
            Assert.Equal(0.5, created.Longitude, 6);

            var update = Input(tag + " güncel", 0.5, 0.5);
            update.Status = IncidentStatus.Resolved;
            var updated = await service.UpdateAsync(created.Id, update);
            Assert.NotNull(updated);
            Assert.Equal(IncidentStatus.Resolved, updated!.Status);

            Assert.Null(await service.GetAsync(int.MaxValue));
            Assert.Null(await service.UpdateAsync(int.MaxValue, Input(tag, 0.5, 0.5)));
            Assert.False(await service.DeleteAsync(int.MaxValue));
        }
        finally
        {
            if (id is { } existing) await service.DeleteAsync(existing);
        }
    }

    [RequiresPostgisFact]
    public async Task LocationColumn_HasGistIndex()
    {
        await using var db = CreateRealContext();

        var definitions = await db.Database
            .SqlQueryRaw<string>("SELECT indexdef AS \"Value\" FROM pg_indexes WHERE tablename = 'incidents'")
            .ToListAsync();

        Assert.Contains(definitions, d => d.Contains("USING gist", StringComparison.OrdinalIgnoreCase));
    }
}
