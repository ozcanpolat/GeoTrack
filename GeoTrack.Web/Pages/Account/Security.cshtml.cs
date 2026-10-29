using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Web.Pages.Account;

[Authorize]
public sealed class SecurityModel(UserManager<ApplicationUser> users, GeoTrackDbContext db,
    SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty, Required] public string Code { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password)] public string CurrentPassword { get; set; } = "";
    public bool IsEnabled { get; private set; }
    public string? AuthenticatorKey { get; private set; }
    public string? OtpAuthUri { get; private set; }
    public string ServerTimeUtc { get; private set; } = "";
    public string? Error { get; private set; }
    [TempData] public string? Success { get; set; }
    [TempData] public string? RecoveryCodes { get; set; }

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostEnableAsync()
    {
        ModelState.Remove(nameof(CurrentPassword));
        if (!ModelState.IsValid) { await LoadAsync(); Error = "Authenticator uygulamasındaki kodu girin."; return Page(); }
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        var code = new string(Code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (code.Length != 6 || code.Any(c => !char.IsAsciiDigit(c)))
        {
            Error = "Authenticator kodu 6 rakamdan oluşmalıdır.";
            await LoadAsync();
            return Page();
        }
        if (!await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, code))
        {
            Error = "Kod eşleşmedi. Ekrandaki kurulum anahtarını yeniden ekleyin, kod türünü zaman tabanlı seçin ve yeni 6 haneli kodu deneyin.";
            await LoadAsync();
            return Page();
        }
        var enabled = await users.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded) { Error = "İki aşamalı doğrulama etkinleştirilemedi."; await LoadAsync(); return Page(); }
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        await users.UpdateSecurityStampAsync(user);
        await signIn.RefreshSignInAsync(user);
        db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "MfaEnabled", EntityType = "User", EntityId = user.Id, Details = "İki aşamalı doğrulama etkinleştirildi.", TimestampUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        RecoveryCodes = string.Join(Environment.NewLine, codes ?? Array.Empty<string>());
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetKeyAsync()
    {
        ModelState.Remove(nameof(Code));
        if (!ModelState.IsValid) { await LoadAsync(); Error = "Yeni kurulum anahtarı için mevcut parolanızı girin."; return Page(); }
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.TwoFactorEnabled) { await LoadAsync(); Error = "Authenticator etkin. Önce iki aşamalı doğrulamayı kapatın."; return Page(); }
        if (!await users.CheckPasswordAsync(user, CurrentPassword)) { await LoadAsync(); Error = "Parola doğrulanamadı."; return Page(); }
        await users.ResetAuthenticatorKeyAsync(user);
        AuthenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        var account = Uri.EscapeDataString("GeoTrack:" + (user.Email ?? user.Id));
        OtpAuthUri = $"otpauth://totp/{account}?secret={AuthenticatorKey}&issuer=GeoTrack&algorithm=SHA1&digits=6&period=30";
        db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "AuthenticatorKeyReset", EntityType = "User", EntityId = user.Id, Details = "Authenticator kurulum anahtarı yenilendi.", TimestampUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Success = "Yeni kurulum anahtarı oluşturuldu. Authenticator uygulamanızda eski GeoTrack kaydını kaldırıp yeni anahtarı ekleyin.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisableAsync()
    {
        ModelState.Remove(nameof(Code));
        if (!ModelState.IsValid) { await LoadAsync(); Error = "Mevcut parolanızı girin."; return Page(); }
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!await users.CheckPasswordAsync(user, CurrentPassword))
        {
            Error = "Parola doğrulanamadı."; await LoadAsync(); return Page();
        }
        var disabled = await users.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded) { Error = "İki aşamalı doğrulama kapatılamadı."; await LoadAsync(); return Page(); }
        await users.ResetAuthenticatorKeyAsync(user);
        await users.UpdateSecurityStampAsync(user);
        await signIn.RefreshSignInAsync(user);
        db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "MfaDisabled", EntityType = "User", EntityId = user.Id, Details = "İki aşamalı doğrulama devre dışı bırakıldı.", TimestampUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        Success = "İki aşamalı doğrulama kapatıldı. Önceki erişim token'ları iptal edildi.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRecoveryCodesAsync()
    {
        ModelState.Remove(nameof(Code));
        if (!ModelState.IsValid) { await LoadAsync(); Error = "Kurtarma kodlarını yenilemek için parolanızı girin."; return Page(); }
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (!user.TwoFactorEnabled || !await users.CheckPasswordAsync(user, CurrentPassword))
        {
            Error = "Parola yanlış veya iki aşamalı doğrulama etkin değil."; await LoadAsync(); return Page();
        }
        RecoveryCodes = string.Join(Environment.NewLine, await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10) ?? Array.Empty<string>());
        db.AuditLogs.Add(new AuditLog { ActorUserId = user.Id, Action = "MfaRecoveryCodesRegenerated", EntityType = "User", EntityId = user.Id, Details = "Kurtarma kodları yenilendi.", TimestampUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Response.Headers.CacheControl = "no-store, no-cache";
        Response.Headers.Pragma = "no-cache";
        ServerTimeUtc = DateTimeOffset.UtcNow.ToString("O");
        var user = await users.GetUserAsync(User);
        IsEnabled = user?.TwoFactorEnabled == true;
        if (user is null || IsEnabled) return;
        AuthenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(AuthenticatorKey))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            AuthenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        }
        if (!string.IsNullOrEmpty(AuthenticatorKey))
        {
            var account = Uri.EscapeDataString("GeoTrack:" + (user.Email ?? user.Id));
            OtpAuthUri = $"otpauth://totp/{account}?secret={AuthenticatorKey}&issuer=GeoTrack&algorithm=SHA1&digits=6&period=30";
        }
    }
}
