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
public sealed class RegisterModel(EmailVerificationService verification, GeoTrackDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "E-posta zorunludur."), EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin."), StringLength(254)] public string Email { get; set; } = "";
    [BindProperty, Required(ErrorMessage = "Parola zorunludur."), MinLength(12, ErrorMessage = "Parola en az 12 karakter olmalıdır."), DataType(DataType.Password)] public string Password { get; set; } = "";
    public IReadOnlyList<string> Errors { get; private set; } = [];
    public bool EmailConfigured => verification.IsConfigured;

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();
        var result = await verification.RegisterAsync(Email, Password, ct);
        switch (result.Status)
        {
            case RegisterStatus.CodeSent:
            case RegisterStatus.CodeCooldown:
                if (result is { IsNewUser: true, User: { } user })
                {
                    db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "AccountRegistered", EntityType = "User", EntityId = user.Id, Details = "Yeni hesap kaydı oluşturuldu.", TimestampUtc = DateTime.UtcNow });
                    await db.SaveChangesAsync(ct);
                }
                TempData["Info"] = result.Status == RegisterStatus.CodeSent
                    ? "Doğrulama kodu e-posta adresinize gönderildi. Kod 10 dakika geçerlidir."
                    : $"Bu adrese kısa süre önce kod gönderildi. E-postanızı kontrol edin; yeni kod için {result.RetryAfterSeconds} sn bekleyin.";
                return RedirectToPage("Verify", new { email = Email.Trim().ToLowerInvariant() });
            case RegisterStatus.AlreadyRegistered:
                Errors = ["Bu e-posta adresi zaten kayıtlı. Giriş yapın veya parolanızı sıfırlayın."];
                break;
            case RegisterStatus.InvalidPassword:
                Errors = result.Errors.Count > 0 ? result.Errors : ["Parola gereksinimleri karşılanmıyor."];
                break;
            case RegisterStatus.LimitReached:
                Errors = ["Bu adres için çok fazla kod istendi. Lütfen bir saat sonra tekrar deneyin."];
                break;
            case RegisterStatus.NotConfigured:
                Errors = ["E-posta doğrulama hizmeti şu an kullanılamıyor. Lütfen daha sonra tekrar deneyin veya yöneticiyle iletişime geçin."];
                break;
            default:
                Errors = ["Doğrulama kodu gönderilemedi. E-posta adresinizi kontrol edip tekrar deneyin; sorun sürerse yöneticiye bildirin."];
                break;
        }
        return Page();
    }
}
