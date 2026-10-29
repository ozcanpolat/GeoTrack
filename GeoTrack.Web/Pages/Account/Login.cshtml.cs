using System.ComponentModel.DataAnnotations;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace GeoTrack.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class LoginModel(SignInManager<ApplicationUser> signIn) : PageModel
{
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    [BindProperty] public string? TwoFactorCode { get; set; }
    [BindProperty] public bool IsRecoveryCode { get; set; }
    [BindProperty] public string? ReturnUrl { get; set; }
    public string? Error { get; private set; }
    public bool RequiresTwoFactor { get; private set; }

    public void OnGet(string? returnUrl = null) => ReturnUrl = returnUrl;
    public async Task<IActionResult> OnPostAsync()
    {
        if (!string.IsNullOrWhiteSpace(TwoFactorCode))
        {
            ModelState.Remove(nameof(Email));
            ModelState.Remove(nameof(Password));
            if (!ModelState.IsValid) return Page();
            var code = TwoFactorCode.Replace(" ", "").Replace("-", "");
            var twoFactor = IsRecoveryCode
                ? await signIn.TwoFactorRecoveryCodeSignInAsync(code)
                : await signIn.TwoFactorAuthenticatorSignInAsync(code, isPersistent: false, rememberClient: false);
            if (!twoFactor.Succeeded)
            {
                RequiresTwoFactor = true;
                Error = "Doğrulama kodu geçersiz veya daha önce kullanılmış.";
                return Page();
            }
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }
        if (!ModelState.IsValid) return Page();
        var result = await signIn.PasswordSignInAsync(Email.Trim(), Password, isPersistent: false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            RequiresTwoFactor = true;
            Password = "";
            ModelState.Remove(nameof(Password));
            return Page();
        }
        if (result.IsNotAllowed) { Error = "E-posta adresiniz henüz doğrulanmamış. \"Doğrulama kodum var\" bağlantısından kodu girin veya yeni kod isteyin."; return Page(); }
        if (!result.Succeeded) { Error = "Giriş başarısız. Bilgileri kontrol edin, e-posta adresinizi doğrulayın veya kısa süre sonra yeniden deneyin."; return Page(); }
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
    }
}
