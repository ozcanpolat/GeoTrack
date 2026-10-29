namespace GeoTrack.Web.Models;

public sealed class UserNotification
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int? IncidentId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
}
