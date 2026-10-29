using System.ComponentModel.DataAnnotations;
using System.Text;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using GeoTrack.Web.Data;

namespace GeoTrack.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class ResetPasswordModel(UserManager<ApplicationUser> users, GeoTrackDbContext db) : PageModel
{
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    [BindProperty, Required] public string Code { get; set; } = "";
    [BindProperty, Required, MinLength(12), DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty, Required, Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
    public IReadOnlyList<string> Errors { get; private set; } = [];
    public bool Completed { get; private set; }

    public void OnGet(string? email, string? code)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Email = email ?? "";
        Code = code ?? "";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is null) Errors = ["Parola yenilenemedi. Bağlantı geçersiz veya süresi dolmuş olabilir."];
        else
        {
            var result = await users.ResetPasswordAsync(user, Decode(Code), Password);
            if (!result.Succeeded) Errors = result.Errors.Select(e => e.Description).ToArray();
            else
            {
                await users.UpdateSecurityStampAsync(user);
                db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "PasswordReset", EntityType = "User", EntityId = user.Id, Details = "Parola sıfırlandı; önceki token'lar iptal edildi.", TimestampUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
                Completed = true;
                return Page();
            }
        }
        Password = ConfirmPassword = "";
        return Page();
    }

    private static string Decode(string token)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token)); }
        catch (FormatException) { return string.Empty; }
    }
}
