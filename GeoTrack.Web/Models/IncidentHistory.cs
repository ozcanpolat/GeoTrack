namespace GeoTrack.Web.Models;

public sealed class IncidentHistory
{
    public long Id { get; set; }
    public int IncidentId { get; set; }
    public string? ChangedByUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? PreviousValue { get; set; }
    public string? NewValue { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
