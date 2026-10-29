using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using GeoTrack.Web.Data;

namespace GeoTrack.Web.Pages.Account;

[AllowAnonymous]
public sealed class ConfirmEmailModel(UserManager<ApplicationUser> users, GeoTrackDbContext db) : PageModel
{
    public string Message { get; private set; } = "Doğrulama bağlantısı işleniyor…";
    public bool Confirmed { get; private set; }

    public async Task OnGetAsync(string? userId, string? code)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            Message = "Doğrulama bağlantısı geçersiz veya süresi dolmuş.";
            return;
        }
        var user = await users.FindByIdAsync(userId);
        if (user is null) { Message = "Doğrulama bağlantısı geçersiz veya süresi dolmuş."; return; }
        var result = await users.ConfirmEmailAsync(user, Decode(code));
        Confirmed = result.Succeeded;
        if (result.Succeeded)
        {
            db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "EmailConfirmed", EntityType = "User", EntityId = user.Id, Details = "E-posta doğrulandı.", TimestampUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        Message = result.Succeeded ? "E-posta adresiniz doğrulandı. Artık giriş yapabilirsiniz." : "Doğrulama bağlantısı geçersiz veya süresi dolmuş.";
    }

    private static string Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return string.Empty; }
    }
}
