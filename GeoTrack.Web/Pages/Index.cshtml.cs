using GeoTrack.Web.DTOs;
using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeoTrack.Web.Pages;

public class IndexModel(IncidentService incidents, ILogger<IndexModel> logger) : PageModel
{
    public StatsDto? Stats { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(CancellationToken ct)
    {
        try
        {
            Stats = await incidents.GetStatsAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Panel istatistikleri okunamadı.");
            Error = "Veritabanına ulaşılamadı. PostgreSQL/PostGIS çalışıyor mu ve migration uygulandı mı?";
        }
    }
}
