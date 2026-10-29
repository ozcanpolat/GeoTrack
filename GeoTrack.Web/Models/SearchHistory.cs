namespace GeoTrack.Web.Models;

public sealed class SearchHistory
{
    public long Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
