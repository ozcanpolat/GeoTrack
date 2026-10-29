using GeoTrack.Web.Data;
using GeoTrack.Web.DTOs;
using GeoTrack.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace GeoTrack.Web.Services;

public class IncidentService(GeoTrackDbContext db)
{
    public const int MaxPageSize = 100;
    public const double MaxRadiusMeters = 50_000;
    public const int MaxNearbyResults = 200;

    public async Task<PagedResult<IncidentDto>> ListAsync(
        IncidentStatus? status, IncidentCategory? category, int page, int pageSize,
        CancellationToken ct = default, string? userId = null, bool isAdmin = false,
        IncidentPriority? priority = null, string? search = null, DateTime? fromUtc = null, DateTime? toUtc = null,
        string? sort = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Incidents.AsNoTracking().AsQueryable();
        if (userId is not null && !isAdmin) query = query.Where(i => i.UserId == userId || i.UserId == null);
        if (status is { } s) query = query.Where(i => i.Status == s);
        if (category is { } c) query = query.Where(i => i.Category == c);
        if (priority is { } p) query = query.Where(i => i.Priority == p);
        if (fromUtc is { } from) query = query.Where(i => i.CreatedAt >= from);
        if (toUtc is { } to) query = query.Where(i => i.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(i => EF.Functions.ILike(i.Title, $"%{term}%") || EF.Functions.ILike(i.Description, $"%{term}%"));
        }

        var total = await query.CountAsync(ct);
        var ordered = sort?.ToLowerInvariant() switch
        {
            "oldest" => query.OrderBy(i => i.CreatedAt).ThenBy(i => i.Id),
            "title" => query.OrderBy(i => i.Title).ThenByDescending(i => i.CreatedAt),
            "priority" => query.OrderByDescending(i => i.Priority).ThenByDescending(i => i.CreatedAt),
            _ => query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id)
        };
        var items = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<IncidentDto>(items.Select(i => ToDto(i)).ToList(), page, pageSize, total);
    }

    public async Task<IncidentDto?> GetAsync(int id, CancellationToken ct = default, string? userId = null, bool isAdmin = false)
    {
        var query = db.Incidents.AsNoTracking().Where(i => i.Id == id);
        if (userId is not null && !isAdmin) query = query.Where(i => i.UserId == userId || i.UserId == null);
        var entity = await query.FirstOrDefaultAsync(ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IncidentDto> CreateAsync(IncidentInput input, CancellationToken ct = default, string? userId = null)
    {
        var now = DateTime.UtcNow;
        var entity = new Incident
        {
            Title = input.Title.Trim(),
            UserId = userId,
            Description = input.Description.Trim(),
            Category = input.Category!.Value,
            Status = IncidentStatus.New,
            Priority = input.Priority,
            Location = GeoFactory.CreatePoint(input.Latitude!.Value, input.Longitude!.Value),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Incidents.Add(entity);
        await db.SaveChangesAsync(ct);
        db.IncidentHistory.Add(new IncidentHistory
        {
            IncidentId = entity.Id, ChangedByUserId = userId, Action = "Created",
            NewValue = IncidentStatus.New.ToString(), CreatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<IncidentDto?> UpdateAsync(int id, IncidentInput input, CancellationToken ct = default, string? userId = null, bool isAdmin = false)
    {
        var query = db.Incidents.Where(i => i.Id == id);
        if (userId is not null && !isAdmin) query = query.Where(i => i.UserId == userId);
        var entity = await query.FirstOrDefaultAsync(ct);
        if (entity is null) return null;

        if (entity.Status != input.Status && !CanTransition(entity.Status, input.Status, isAdmin))
            throw new InvalidIncidentTransitionException(entity.Status, input.Status);
        if (entity.Status != input.Status && input.Status == IncidentStatus.Assigned && entity.AssignedToUserId is null)
            throw new IncidentAssignmentRequiredException();

        var previousStatus = entity.Status;
        entity.Title = input.Title.Trim();
        entity.Description = input.Description.Trim();
        entity.Category = input.Category!.Value;
        entity.Status = input.Status;
        entity.ResolvedAt = input.Status == IncidentStatus.Resolved
            ? entity.ResolvedAt ?? DateTime.UtcNow
            : null;
        entity.Priority = input.Priority;
        entity.Location = GeoFactory.CreatePoint(input.Latitude!.Value, input.Longitude!.Value);
        entity.UpdatedAt = DateTime.UtcNow;

        if (previousStatus != entity.Status)
            db.IncidentHistory.Add(new IncidentHistory
            {
                IncidentId = entity.Id, ChangedByUserId = userId, Action = "StatusChanged",
                PreviousValue = previousStatus.ToString(), NewValue = entity.Status.ToString(), CreatedAtUtc = entity.UpdatedAt
            });

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<IncidentDto?> ApplyWorkflowAsync(int id, IncidentStatus nextStatus, string? assignedToUserId,
        bool assignmentProvided, bool isAdmin, string? userId, CancellationToken ct = default)
    {
        var query = db.Incidents.Where(i => i.Id == id);
        if (userId is not null && !isAdmin) query = query.Where(i => i.UserId == userId);
        var entity = await query.FirstOrDefaultAsync(ct);
        if (entity is null) return null;
        if (!isAdmin && assignmentProvided) throw new IncidentAssignmentForbiddenException();
        if (nextStatus != entity.Status && !CanTransition(entity.Status, nextStatus, isAdmin))
            throw new InvalidIncidentTransitionException(entity.Status, nextStatus);

        var previousStatus = entity.Status;
        var previousAssignee = entity.AssignedToUserId;
        if (assignmentProvided) entity.AssignedToUserId = assignedToUserId;
        if (nextStatus == IncidentStatus.Assigned && string.IsNullOrWhiteSpace(entity.AssignedToUserId))
            throw new IncidentAssignmentRequiredException();
        entity.Status = nextStatus;
        entity.ResolvedAt = nextStatus == IncidentStatus.Resolved ? DateTime.UtcNow : null;
        entity.UpdatedAt = DateTime.UtcNow;

        if (previousStatus != nextStatus)
            db.IncidentHistory.Add(new IncidentHistory
            {
                IncidentId = id, ChangedByUserId = userId, Action = "StatusChanged",
                PreviousValue = previousStatus.ToString(), NewValue = nextStatus.ToString(), CreatedAtUtc = entity.UpdatedAt
            });
        if (previousAssignee != entity.AssignedToUserId)
            db.IncidentHistory.Add(new IncidentHistory
            {
                IncidentId = id, ChangedByUserId = userId, Action = "AssignmentChanged",
                PreviousValue = previousAssignee is null ? "Unassigned" : "Assigned",
                NewValue = entity.AssignedToUserId is null ? "Unassigned" : "Assigned", CreatedAtUtc = entity.UpdatedAt
            });
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    private static bool CanTransition(IncidentStatus from, IncidentStatus to, bool isAdmin) => (from, to) switch
    {
        (IncidentStatus.New, IncidentStatus.InProgress or IncidentStatus.Assigned) => true,
        (IncidentStatus.InProgress, IncidentStatus.Assigned or IncidentStatus.Resolved) => true,
        (IncidentStatus.Assigned, IncidentStatus.InProgress or IncidentStatus.Resolved) => true,
        (IncidentStatus.Resolved, IncidentStatus.InProgress) => isAdmin,
        _ => false
    };

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default, string? userId = null, bool isAdmin = false)
    {
        var query = db.Incidents.Where(i => i.Id == id);
        if (userId is not null && !isAdmin) query = query.Where(i => i.UserId == userId);
        var entity = await query.FirstOrDefaultAsync(ct);
        if (entity is null) return false;

        db.Incidents.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Verilen noktanın radiusMeters metre çevresindeki kayıtlar.
    /// Filtre veritabanında çalışır: IsWithinDistance → ST_DWithin (geography, metre).
    /// </summary>
    public async Task<IReadOnlyList<IncidentDto>> NearbyAsync(
        double latitude, double longitude, double radiusMeters, CancellationToken ct = default, string? userId = null, bool isAdmin = false)
    {
        var center = GeoFactory.CreatePoint(latitude, longitude);

        var query = db.Incidents.AsNoTracking().AsQueryable();
        if (userId is not null && !isAdmin) query = query.Where(i => i.UserId == userId || i.UserId == null);
        var rows = await query
            .Where(i => i.Location.IsWithinDistance(center, radiusMeters))
            .Select(i => new { Incident = i, Distance = i.Location.Distance(center) })
            .OrderBy(x => x.Distance)
            .Take(MaxNearbyResults)
            .ToListAsync(ct);

        return rows.Select(r => ToDto(r.Incident, r.Distance)).ToList();
    }

    public async Task<StatsDto> GetStatsAsync(CancellationToken ct = default, string? userId = null, bool isAdmin = false)
    {
        var query = db.Incidents.AsNoTracking().AsQueryable();
        if (userId is not null && !isAdmin) query = query.Where(i => i.UserId == userId || i.UserId == null);
        var grouped = await query
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(IncidentStatus s) => grouped.FirstOrDefault(g => g.Status == s)?.Count ?? 0;

        var criticalOpen = await query.CountAsync(
            i => i.Priority == IncidentPriority.Critical && i.Status != IncidentStatus.Resolved, ct);

        return new StatsDto(
            grouped.Sum(g => g.Count),
            Count(IncidentStatus.New),
            Count(IncidentStatus.InProgress),
            Count(IncidentStatus.Assigned),
            Count(IncidentStatus.Resolved),
            criticalOpen);
    }

    private static IncidentDto ToDto(Incident i, double? distance = null) => new(
        i.Id, i.Title, i.Description, i.Category, i.Status, i.Priority,
        DateTime.SpecifyKind(i.CreatedAt, DateTimeKind.Utc),
        DateTime.SpecifyKind(i.UpdatedAt, DateTimeKind.Utc),
        i.Location.Y, i.Location.X,
        distance is null ? null : Math.Round(distance.Value, 1), i.AssignedToUserId,
        i.ResolvedAt is null ? null : DateTime.SpecifyKind(i.ResolvedAt.Value, DateTimeKind.Utc));
}

public sealed class InvalidIncidentTransitionException(IncidentStatus from, IncidentStatus to)
    : Exception($"Olay durumu {from} değerinden {to} değerine geçirilemez.");

public sealed class IncidentAssignmentForbiddenException()
    : Exception("Olay atamasını yalnızca yönetici değiştirebilir.");

public sealed class IncidentAssignmentRequiredException()
    : Exception("Atanmış duruma geçmek için etkin bir kullanıcı seçin.");
