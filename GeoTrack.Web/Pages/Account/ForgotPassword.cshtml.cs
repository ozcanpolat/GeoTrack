using System.ComponentModel.DataAnnotations;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services.Email;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace GeoTrack.Web.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("auth")]
public sealed class ForgotPasswordModel(UserManager<ApplicationUser> users, AccountEmailService email) : PageModel
{
    [BindProperty, Required, EmailAddress] public string Email { get; set; } = "";
    public bool Submitted { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        if (!ModelState.IsValid) return Page();
        var user = await users.FindByEmailAsync(Email.Trim());
        if (user is { EmailConfirmed: true, IsActive: true })
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await email.SendPasswordResetAsync(user, token, Request, ct);
        }
        Submitted = true;
        return Page();
    }
}
