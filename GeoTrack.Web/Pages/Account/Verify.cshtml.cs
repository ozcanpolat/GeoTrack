using System.ComponentModel.DataAnnotations;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace GeoTrack.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class VerifyModel(EmailVerificationService verification, GeoTrackDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "E-posta zorunludur."), EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")] public string Email { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Doğrulama kodunu girin."), RegularExpression(@"^\s*\d{6}\s*$", ErrorMessage = "Kod 6 haneli bir sayı olmalıdır.")] public string Code { get; set; } = "";
    public string? Error { get; private set; }
    public string? Info { get; private set; }

    public void OnGet(string? email)
    {
        Email = email ?? "";
        Info = TempData["Info"] as string;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        var (status, user) = await verification.VerifyAsync(Email, Code.Trim());
        switch (status)
        {
            case VerifyStatus.Success:
                db.AuditLogs.Add(new AuditLog { ActorUserId = user!.Id, Action = "EmailConfirmed", EntityType = "User", EntityId = user.Id, Details = "E-posta doğrulama koduyla doğrulandı.", TimestampUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
                TempData["Success"] = "E-posta adresiniz doğrulandı. Şimdi giriş yapabilirsiniz.";
                return RedirectToPage("Login");
            case VerifyStatus.AlreadyConfirmed:
                TempData["Success"] = "Bu e-posta adresi zaten doğrulanmış. Giriş yapabilirsiniz.";
                return RedirectToPage("Login");
            case VerifyStatus.Expired:
                Error = "Kodun süresi dolmuş veya bu adres için aktif kod yok. Yeni kod isteyin.";
                break;
            case VerifyStatus.TooManyAttempts:
                Error = "Çok fazla hatalı deneme yapıldı. Yeni kod isteyin.";
                break;
            default:
                Error = "Kod geçersiz. Kontrol edip tekrar deneyin.";
                break;
        }
        Code = "";
        ModelState.Remove(nameof(Code));
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken ct)
    {
        ModelState.Remove(nameof(Code));
        if (string.IsNullOrWhiteSpace(Email) || !new EmailAddressAttribute().IsValid(Email))
        {
            Error = "Önce geçerli bir e-posta adresi girin.";
            return Page();
        }
        var result = await verification.ResendAsync(Email, ct);
        switch (result.Status)
        {
            case IssueStatus.Sent: Info = "Adres doğrulama bekliyorsa yeni kod gönderildi. Kod 10 dakika geçerlidir."; break;
            case IssueStatus.Cooldown: Error = $"Yeni kod için {result.RetryAfterSeconds} saniye bekleyin."; break;
            case IssueStatus.LimitReached: Error = "Çok fazla kod istendi. Lütfen bir saat sonra tekrar deneyin."; break;
            default: Error = "Kod şu an gönderilemedi. Lütfen daha sonra tekrar deneyin."; break;
        }
        Code = "";
        return Page();
    }
}
