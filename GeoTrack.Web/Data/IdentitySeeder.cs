using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace GeoTrack.Web.Data;

public static class IdentitySeeder
{
    public const string AdminRole = "Admin";
    public const string UserRole = "User";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { AdminRole, UserRole })
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var password = config["Admin:Password"];
        var email = config["Admin:Email"];
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning("Admin account bootstrap skipped: set ADMIN_EMAIL and ADMIN_PASSWORD in the environment.");
            return;
        }
        email = email.Trim().ToLowerInvariant();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(admin, password);
            if (!created.Succeeded)
                throw new InvalidOperationException("Admin seed failed: " + string.Join("; ", created.Errors.Select(e => e.Code)));
        }
        if (!await users.IsInRoleAsync(admin, AdminRole))
        {
            var roleResult = await users.AddToRoleAsync(admin, AdminRole);
            if (!roleResult.Succeeded)
                throw new InvalidOperationException("Admin role assignment failed.");
        }
    }
}
