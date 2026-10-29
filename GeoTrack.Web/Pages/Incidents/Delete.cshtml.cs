using GeoTrack.Web.DTOs;
using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Web.Pages.Incidents;

public class DeleteModel(IncidentService incidents, ILogger<DeleteModel> logger, GeoTrackDbContext db, AuditLogService audit, PhotoStore photos) : PageModel
{
    public IncidentDto? Incident { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        Incident = await incidents.GetAsync(id, ct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.IsInRole("Admin"));
        return Incident is null ? NotFound() : Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken ct)
    {
        try
        {
            var deleted = await incidents.DeleteAsync(id, ct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.IsInRole("Admin"));
            if (!deleted)
            {
                return NotFound();
            }

            photos.DeleteAll(id);
            audit.Add(User.FindFirstValue(ClaimTypes.NameIdentifier), "IncidentDeleted", "Incident", id.ToString(), "Olay kaydı silindi.");
            await db.SaveChangesAsync(ct);
            TempData["Success"] = "Arıza kaydı silindi.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Arıza kaydı silinemedi: {Id}", id);
            Incident = await incidents.GetAsync(id, ct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.IsInRole("Admin"));
            ModelState.AddModelError(string.Empty, "Silme sırasında bir hata oluştu.");
            return Page();
        }
    }
}
