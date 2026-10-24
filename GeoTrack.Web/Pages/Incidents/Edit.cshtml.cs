using GeoTrack.Web.DTOs;
using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeoTrack.Web.Pages.Incidents;

public class EditModel(IncidentService incidents, ILogger<EditModel> logger) : PageModel
{
    [BindProperty]
    public IncidentInput Input { get; set; } = new();

    public int IncidentId { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        var dto = await incidents.GetAsync(id, ct);
        if (dto is null)
        {
            return NotFound();
        }

        IncidentId = id;
        Input = new IncidentInput
        {
            Title = dto.Title,
            Description = dto.Description,
            Category = dto.Category,
            Status = dto.Status,
            Priority = dto.Priority,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        IncidentId = id;

        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var updated = await incidents.UpdateAsync(id, Input, ct);
            if (updated is null)
            {
                return NotFound();
            }

            TempData["Success"] = "Arıza kaydı güncellendi.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Arıza kaydı güncellenemedi: {Id}", id);
            ModelState.AddModelError(string.Empty,
                "Güncelleme sırasında bir hata oluştu. Veritabanı bağlantısını kontrol edip tekrar deneyin.");
            return Page();
        }
    }
}
