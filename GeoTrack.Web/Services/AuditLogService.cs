using GeoTrack.Web.Data;
using GeoTrack.Web.Models;

namespace GeoTrack.Web.Services;

public sealed class AuditLogService(GeoTrackDbContext db)
{
    public void Add(string? actorUserId, string action, string entityType, string? entityId, string? details = null)
    {
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            TimestampUtc = DateTime.UtcNow
        });
    }

}
