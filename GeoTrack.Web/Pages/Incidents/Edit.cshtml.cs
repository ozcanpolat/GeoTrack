using GeoTrack.Web.DTOs;
using GeoTrack.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Security.Claims;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace GeoTrack.Web.Pages.Incidents;

public class EditModel(IncidentService incidents, ILogger<EditModel> logger, GeoTrackDbContext db,
    UserManager<ApplicationUser> users, AuditLogService audit, IHubContext<GeoTrackHub> hub, PhotoStore photoStore) : PageModel
{
    [BindProperty]
    public IncidentInput Input { get; set; } = new();

    public int IncidentId { get; private set; }
    public IncidentDto? Incident { get; private set; }
    public IReadOnlyList<IncidentHistory> History { get; private set; } = [];
    public IReadOnlyList<AssignableUser> AssignableUsers { get; private set; } = [];
    public IReadOnlyList<PhotoInfo> Photos { get; private set; } = [];
    public bool IsAdmin => User.IsInRole("Admin");

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken ct)
    {
        Incident = await incidents.GetAsync(id, ct, User.FindFirstValue(ClaimTypes.NameIdentifier), IsAdmin);
        if (Incident is null)
        {
            return NotFound();
        }

        IncidentId = id;
        Input = new IncidentInput
        {
            Title = Incident.Title,
            Description = Incident.Description,
            Category = Incident.Category,
            Status = Incident.Status,
            Priority = Incident.Priority,
            Latitude = Incident.Latitude,
            Longitude = Incident.Longitude
        };
        await LoadWorkflowDataAsync(id, ct);
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
            var updated = await incidents.UpdateAsync(id, Input, ct, User.FindFirstValue(ClaimTypes.NameIdentifier), User.IsInRole("Admin"));
            if (updated is null)
            {
                return NotFound();
            }

            audit.Add(User.FindFirstValue(ClaimTypes.NameIdentifier), "IncidentUpdated", "Incident", id.ToString(), "Olay bilgileri güncellendi.");
            await db.SaveChangesAsync(ct);
            TempData["Success"] = "Arıza kaydı güncellendi.";
            return RedirectToPage("Index");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Arıza kaydı güncellenemedi: {Id}", id);
            ModelState.AddModelError(string.Empty,
                ex is InvalidIncidentTransitionException or IncidentAssignmentRequiredException
                    ? ex.Message
                    : "Güncelleme sırasında bir hata oluştu. Veritabanı bağlantısını kontrol edip tekrar deneyin.");
            Incident = await incidents.GetAsync(id, ct, User.FindFirstValue(ClaimTypes.NameIdentifier), IsAdmin);
            await LoadWorkflowDataAsync(id, ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostWorkflowAsync(int id, IncidentStatus status, string? assignedToUserId, List<IFormFile>? resolutionPhotos, CancellationToken ct)
    {
        IncidentId = id;
        try
        {
            Incident = await incidents.ApplyWorkflowAsync(id, status, assignedToUserId, IsAdmin,
                IsAdmin, User.FindFirstValue(ClaimTypes.NameIdentifier), ct);
            if (Incident is null) return NotFound();
            var ownerId = await db.Incidents.AsNoTracking().Where(i => i.Id == id).Select(i => i.UserId).FirstOrDefaultAsync(ct);
            audit.Add(User.FindFirstValue(ClaimTypes.NameIdentifier), "IncidentWorkflowUpdated", "Incident", id.ToString(), "Olay durumu veya ataması güncellendi.");
            if (ownerId is not null)
                db.Notifications.Add(new UserNotification { UserId = ownerId, IncidentId = id, Message = $"Olay durumu güncellendi: {Incident.Title} · {Incident.Status}" });
            await db.SaveChangesAsync(ct);
            if (ownerId is not null)
                await hub.Clients.Group($"user:{ownerId}").SendAsync("notification", new { message = "Olay durumunuz güncellendi.", incidentId = id }, ct);
            TempData["Success"] = "Olay iş akışı güncellendi.";
            if (IsAdmin && status == IncidentStatus.Resolved && resolutionPhotos is { Count: > 0 })
            {
                var photoErrors = new List<string>();
                foreach (var file in resolutionPhotos.Take(PhotoStore.MaxPerKind))
                {
                    var (ok, error) = await photoStore.SaveAsync(id, PhotoStore.Resolution, file, ct); // yalnızca çözüm klasörüne yazar
                    if (!ok && error is not null) photoErrors.Add(error);
                }
                if (photoErrors.Count > 0) TempData["Error"] = "Durum güncellendi ancak bazı çözüm fotoğrafları eklenemedi: " + string.Join(" ", photoErrors);
            }
            return RedirectToPage(new { id });
        }
        catch (Exception ex) when (ex is InvalidIncidentTransitionException or IncidentAssignmentRequiredException or IncidentAssignmentForbiddenException)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Incident = await incidents.GetAsync(id, ct, User.FindFirstValue(ClaimTypes.NameIdentifier), IsAdmin);
            await LoadWorkflowDataAsync(id, ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPhotoAsync(int id, string kind, List<IFormFile> files, CancellationToken ct)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (await incidents.GetAsync(id, ct, uid, IsAdmin) is null) return NotFound();
        if (!PhotoStore.IsValidKind(kind) || (kind == PhotoStore.Resolution && !IsAdmin)) return Forbid();
        var errors = new List<string>();
        var added = 0;
        foreach (var file in files.Take(PhotoStore.MaxPerKind))
        {
            var (ok, error) = await photoStore.SaveAsync(id, kind, file, ct);
            if (ok) added++; else if (error is not null) errors.Add(error);
        }
        if (added > 0)
        {
            audit.Add(uid, "IncidentPhotoAdded", "Incident", id.ToString(), "Olaya fotoğraf eklendi.");
            var ownerId = await db.Incidents.AsNoTracking().Where(i => i.Id == id).Select(i => i.UserId).FirstOrDefaultAsync(ct);
            if (kind == PhotoStore.Resolution && ownerId is not null)
                db.Notifications.Add(new UserNotification { UserId = ownerId, IncidentId = id, Message = $"#{id} numaralı olaya çözüm fotoğrafı eklendi." });
            await db.SaveChangesAsync(ct);
            TempData["Success"] = $"{added} fotoğraf eklendi.";
        }
        if (errors.Count > 0) TempData["Error"] = string.Join(" ", errors);
        else if (added == 0) TempData["Error"] = "Fotoğraf seçilmedi.";
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostDeletePhotoAsync(int id, string name, CancellationToken ct)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (await incidents.GetAsync(id, ct, uid, IsAdmin) is null) return NotFound();
        if (name.StartsWith("r_") && !IsAdmin) return Forbid();
        if (photoStore.Delete(id, name))
        {
            audit.Add(uid, "IncidentPhotoDeleted", "Incident", id.ToString(), "Olay fotoğrafı silindi.");
            await db.SaveChangesAsync(ct);
            TempData["Success"] = "Fotoğraf silindi.";
        }
        return RedirectToPage(new { id });
    }

    private async Task LoadWorkflowDataAsync(int id, CancellationToken ct)
    {
        Photos = photoStore.List(id);
        History = await db.IncidentHistory.AsNoTracking().Where(h => h.IncidentId == id)
            .OrderByDescending(h => h.CreatedAtUtc).Take(30).ToListAsync(ct);
        if (IsAdmin)
        {
            var activeUsers = await users.Users.AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.Email).ToListAsync(ct);
            var rows = new List<AssignableUser>();
            foreach (var user in activeUsers)
                if (await users.IsInRoleAsync(user, IdentitySeeder.UserRole) || await users.IsInRoleAsync(user, IdentitySeeder.AdminRole))
                    rows.Add(new AssignableUser(user.Id, user.Email ?? user.UserName ?? ""));
            AssignableUsers = rows;
        }
    }
}

public sealed record AssignableUser(string Id, string Email);
