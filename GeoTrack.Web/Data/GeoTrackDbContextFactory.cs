using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoTrack.Web.Data;

/// <summary>
/// Yalnızca "dotnet ef" araçları içindir (migrations add / database update). Araçlar bu fabrikayı bulunca
/// uygulamayı (Program.cs) çalıştırmaz; böylece açılıştaki migration/seed bloğu araç çalışırken tetiklenmez.
/// Bağlantı dizesi çalışma zamanındaki ile aynı kaynaktan okunur: appsettings*.json ve ortam değişkenleri.
/// </summary>
public class GeoTrackDbContextFactory : IDesignTimeDbContextFactory<GeoTrackDbContext>
{
    public GeoTrackDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("GeoTrack");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:GeoTrack bulunamadı. Komutu GeoTrack.Web klasöründe çalıştırın " +
                "veya ConnectionStrings__GeoTrack ortam değişkenini ayarlayın.");
        }

        var options = new DbContextOptionsBuilder<GeoTrackDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;

        return new GeoTrackDbContext(options);
    }
}
