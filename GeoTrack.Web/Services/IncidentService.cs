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
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Incidents.AsNoTracking().AsQueryable();
        if (status is { } s) query = query.Where(i => i.Status == s);
        if (category is { } c) query = query.Where(i => i.Category == c);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<IncidentDto>(items.Select(i => ToDto(i)).ToList(), page, pageSize, total);
    }

    public async Task<IncidentDto?> GetAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Incidents.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        return entity is null ? null : ToDto(entity);
    }

    public async Task<IncidentDto> CreateAsync(IncidentInput input, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var entity = new Incident
        {
            Title = input.Title.Trim(),
            Description = input.Description.Trim(),
            Category = input.Category!.Value,
            Status = input.Status,
            Priority = input.Priority,
            Location = GeoFactory.CreatePoint(input.Latitude!.Value, input.Longitude!.Value),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Incidents.Add(entity);
        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<IncidentDto?> UpdateAsync(int id, IncidentInput input, CancellationToken ct = default)
    {
        var entity = await db.Incidents.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (entity is null) return null;

        entity.Title = input.Title.Trim();
        entity.Description = input.Description.Trim();
        entity.Category = input.Category!.Value;
        entity.Status = input.Status;
        entity.Priority = input.Priority;
        entity.Location = GeoFactory.CreatePoint(input.Latitude!.Value, input.Longitude!.Value);
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var entity = await db.Incidents.FirstOrDefaultAsync(i => i.Id == id, ct);
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
        double latitude, double longitude, double radiusMeters, CancellationToken ct = default)
    {
        var center = GeoFactory.CreatePoint(latitude, longitude);

        var rows = await db.Incidents
            .AsNoTracking()
            .Where(i => i.Location.IsWithinDistance(center, radiusMeters))
            .Select(i => new { Incident = i, Distance = i.Location.Distance(center) })
            .OrderBy(x => x.Distance)
            .Take(MaxNearbyResults)
            .ToListAsync(ct);

        return rows.Select(r => ToDto(r.Incident, r.Distance)).ToList();
    }

    public async Task<StatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var grouped = await db.Incidents
            .AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(IncidentStatus s) => grouped.FirstOrDefault(g => g.Status == s)?.Count ?? 0;

        var criticalOpen = await db.Incidents.CountAsync(
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
        distance is null ? null : Math.Round(distance.Value, 1));
}
