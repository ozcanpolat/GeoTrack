using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using GeoTrack.Web.Services.Email;

namespace GeoTrack.Web.Pages.Admin;

[Authorize(Roles = IdentitySeeder.AdminRole)]
public sealed class IndexModel(GeoTrackDbContext db, UserManager<ApplicationUser> users, IEmailSender emailSender) : PageModel
{
    public int Users { get; private set; }
    public int Incidents { get; private set; }
    public int OpenIncidents { get; private set; }
    public int UnreadNotifications { get; private set; }
    public bool EmailConfigured => emailSender.IsConfigured;
    public IReadOnlyList<string> EmailMissing => emailSender.MissingSettings;
    public IReadOnlyList<AdminUserRow> UserList { get; private set; } = [];
    public IReadOnlyList<AuditLog> RecentAudit { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken ct)
    {
        Users = await db.Users.CountAsync(ct);
        Incidents = await db.Incidents.CountAsync(ct);
        OpenIncidents = await db.Incidents.CountAsync(i => i.Status != IncidentStatus.Resolved, ct);
        UnreadNotifications = await db.Notifications.CountAsync(n => !n.IsRead, ct);
        RecentAudit = await db.AuditLogs.AsNoTracking().OrderByDescending(a => a.TimestampUtc).Take(25).ToListAsync(ct);
        var allUsers = await users.Users.OrderBy(u => u.Email).ToListAsync(ct);
        var rows = new List<AdminUserRow>(allUsers.Count);
        foreach (var user in allUsers)
            rows.Add(new AdminUserRow(user.Id, user.Email ?? "", user.IsActive, user.CreatedAtUtc, await users.GetRolesAsync(user)));
        UserList = rows;
    }

    public async Task<IActionResult> OnPostSetActiveAsync(string userId, bool isActive, CancellationToken ct)
    {
        if (!isActive && User.FindFirstValue(ClaimTypes.NameIdentifier) == userId)
        {
            TempData["Error"] = "Kendi etkin yönetici hesabınızı kapatamazsınız.";
            return RedirectToPage();
        }
        var target = await users.FindByIdAsync(userId);
        if (target is null) return NotFound();
        if (!isActive && await users.IsInRoleAsync(target, IdentitySeeder.AdminRole))
        {
            var admins = await users.GetUsersInRoleAsync(IdentitySeeder.AdminRole);
            if (admins.Count(u => u.IsActive) <= 1)
            {
                TempData["Error"] = "Son etkin yöneticiyi devre dışı bırakamazsınız.";
                return RedirectToPage();
            }
        }
        target.IsActive = isActive;
        await users.UpdateSecurityStampAsync(target);
        var update = await users.UpdateAsync(target);
        if (!update.Succeeded) TempData["Error"] = string.Join("; ", update.Errors.Select(e => e.Description));
        else
        {
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier),
                Action = "UserActivationChanged", EntityType = "User", EntityId = userId,
                Details = $"Etkinlik durumu {isActive} olarak ayarlandı.", TimestampUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        return RedirectToPage();
    }
}

public sealed record AdminUserRow(string Id, string Email, bool IsActive, DateTime CreatedAtUtc, IList<string> Roles);
