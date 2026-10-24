using GeoTrack.Web.DTOs;
using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeoTrack.Web.Pages.Incidents;

public class DeleteModel(IncidentService incidents, ILogger<DeleteModel> logger) : PageModel
{
    public IncidentDto? Incident { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        Incident = await incidents.GetAsync(id, ct);
        return Incident is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        try
        {
            var deleted = await incidents.DeleteAsync(id, ct);
            if (!deleted)
            {
                return NotFound();
            }

            TempData["Success"] = "Arıza kaydı silindi.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Arıza kaydı silinemedi: {Id}", id);
            Incident = await incidents.GetAsync(id, ct);
            ModelState.AddModelError(string.Empty, "Silme sırasında bir hata oluştu.");
            return Page();
        }
    }
}
