using NetTopologySuite.Geometries;

namespace GeoTrack.Web.Models;

public class Incident
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    public string? AssignedToUserId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public IncidentCategory Category { get; set; }

    public IncidentStatus Status { get; set; } = IncidentStatus.New;

    public IncidentPriority Priority { get; set; } = IncidentPriority.Medium;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ResolvedAt { get; set; }


    public Point Location { get; set; } = null!;
}
