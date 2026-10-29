using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using GeoTrack.Web.Models;

namespace GeoTrack.Web.Pages.Account;

public sealed class LogoutModel(SignInManager<ApplicationUser> signIn) : PageModel
{
    public async Task<IActionResult> OnPostAsync()
    {
        await signIn.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}
