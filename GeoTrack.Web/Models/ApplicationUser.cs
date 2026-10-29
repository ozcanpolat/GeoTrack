using Microsoft.AspNetCore.Identity;

namespace GeoTrack.Web.Models;

public sealed class ApplicationUser : IdentityUser
{
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
