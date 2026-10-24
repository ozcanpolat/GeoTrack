using NetTopologySuite.Geometries;

namespace GeoTrack.Web.Models;

public class Incident
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public IncidentCategory Category { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.New;

    public IncidentPriority Priority { get; set; } = IncidentPriority.Medium;

    /// <summary>UTC oluşturulma zamanı.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC son güncelleme zamanı.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// PostGIS geography(Point, 4326). Konumun tek doğru kaynağı budur.
    /// X = boylam (longitude), Y = enlem (latitude).
    /// </summary>
    public Point Location { get; set; } = null!;
}
