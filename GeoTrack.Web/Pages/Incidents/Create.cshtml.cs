using GeoTrack.Web.DTOs;
using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GeoTrack.Web.Pages.Incidents;

public class CreateModel(IncidentService incidents, ILogger<CreateModel> logger) : PageModel
{
    [BindProperty]
    public IncidentInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            await incidents.CreateAsync(Input, ct);
            TempData["Success"] = "Arıza kaydı oluşturuldu.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Arıza kaydı oluşturulamadı.");
            ModelState.AddModelError(string.Empty,
                "Kayıt sırasında bir hata oluştu. Veritabanı bağlantısını kontrol edip tekrar deneyin.");
            return Page();
        }
    }
}
