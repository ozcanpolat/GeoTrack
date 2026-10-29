using Microsoft.Extensions.Caching.Memory;
using GeoTrack.Web.DTOs;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.SignalR;
using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;

namespace GeoTrack.Web.Pages.Incidents;

public class CreateModel(IncidentService incidents, ILogger<CreateModel> logger, AuditLogService audit,
    GeoTrack.Web.Data.GeoTrackDbContext db, IHubContext<GeoTrackHub> hub, PhotoStore photoStore,
    IMemoryCache cache) : PageModel
{
    [BindProperty]
    public IncidentInput Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Photos { get; set; } = [];

    /// <summary>Çift gönderimi engelleyen tek kullanımlık form anahtarı.</summary>
    [BindProperty]
    public string? SubmitToken { get; set; }

    public bool PhotoReselect { get; private set; }

    public void OnGet() => SubmitToken = Guid.NewGuid().ToString("N");

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (Input.Status != IncidentStatus.New)
            ModelState.AddModelError("Input.Status", "Yeni olaylar New durumunda oluşturulur; sonraki durumları iş akışından seçin.");
        if (!ModelState.IsValid)
        {
            PhotoReselect = Photos.Count > 0;
            return Page();
        }

        var tokenKey = $"create:{User.FindFirstValue(ClaimTypes.NameIdentifier)}:{SubmitToken}";
        if (!string.IsNullOrEmpty(SubmitToken))
        {
            if (cache.TryGetValue(tokenKey, out _))
            {
                TempData["Success"] = "Arıza kaydı zaten oluşturuldu.";
                return RedirectToPage("Index");
            }
            cache.Set(tokenKey, true, TimeSpan.FromMinutes(10));
        }

        try
        {
            var created = await incidents.CreateAsync(Input, ct, User.FindFirstValue(ClaimTypes.NameIdentifier));
            var photoErrors = new List<string>();
            foreach (var photo in Photos.Take(PhotoStore.MaxPerKind))
            {
                var (saved, photoError) = await photoStore.SaveAsync(created.Id, PhotoStore.Citizen, photo, ct);
                if (!saved && photoError is not null) photoErrors.Add(photoError);
            }
            if (photoErrors.Count > 0) TempData["Error"] = "Olay kaydedildi ancak bazı fotoğraflar eklenemedi: " + string.Join(" ", photoErrors);
            audit.Add(User.FindFirstValue(ClaimTypes.NameIdentifier), "IncidentCreated", "Incident", created.Id.ToString(), "Olay kaydı oluşturuldu.");
            var owner = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (owner is not null)
                db.Notifications.Add(new UserNotification { UserId = owner, IncidentId = created.Id, Message = $"Olay kaydı oluşturuldu: {created.Title}" });
            await db.SaveChangesAsync(ct);
            if (owner is not null)
                await hub.Clients.Group($"user:{owner}").SendAsync("notification", new { message = "Olay kaydınız oluşturuldu.", incidentId = created.Id }, ct);
            TempData["Success"] = "Arıza kaydı oluşturuldu.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cache.Remove(tokenKey); // hata sonrası yeniden denemeye izin ver
            PhotoReselect = Photos.Count > 0;
            logger.LogError(ex, "Arıza kaydı oluşturulamadı.");
            ModelState.AddModelError(string.Empty,
                "Kayıt sırasında bir hata oluştu. Veritabanı bağlantısını kontrol edip tekrar deneyin.");
            return Page();
        }
    }
}
