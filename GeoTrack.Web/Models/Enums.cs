namespace GeoTrack.Web.Models;

public enum IncidentCategory
{
    Road,
    WaterInfrastructure,
    Electricity,
    Waste,
    Other
}

public enum IncidentStatus
{
    New,
    InProgress,
    Assigned,
    Resolved
}

public enum IncidentPriority
{
    Low,
    Medium,
    High,
    Critical
}
