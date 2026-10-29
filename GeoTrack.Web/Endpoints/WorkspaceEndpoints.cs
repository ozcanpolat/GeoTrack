using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GeoTrack.Web.Services;

namespace GeoTrack.Web.Endpoints;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/search", async (string q, ClaimsPrincipal principal, GeoTrackDbContext db, CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
            q = q.Trim();
            if (q.Length is < 2 or > 150) return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Arama 2-150 karakter olmalı."] });
            db.SearchHistory.Add(new SearchHistory { UserId = userId, Query = q });
            await db.SaveChangesAsync(ct);
            var query = db.Incidents.AsNoTracking().AsQueryable();
            if (!principal.IsInRole(IdentitySeeder.AdminRole)) query = query.Where(i => i.UserId == userId || i.UserId == null);
            var rows = await query.Where(i => EF.Functions.ILike(i.Title, $"%{q}%") || EF.Functions.ILike(i.Description, $"%{q}%"))
                .OrderByDescending(i => i.CreatedAt).Take(100).ToListAsync(ct);
            // NTS reads the geography coordinates client-side; ST_X/ST_Y accept geometry rather than geography.
            return Results.Ok(rows.Select(i => new { i.Id, i.Title, i.Description, i.Status, i.Priority, Latitude = i.Location.Y, Longitude = i.Location.X }));
        }).WithName("SearchIncidents");

        api.MapGet("/search-history", async (ClaimsPrincipal p, GeoTrackDbContext db, CancellationToken ct) =>
            Results.Ok(await db.SearchHistory.AsNoTracking().Where(s => s.UserId == p.FindFirstValue(ClaimTypes.NameIdentifier))
                .OrderByDescending(s => s.CreatedAt).Take(50).Select(s => new { s.Query, s.CreatedAt }).ToListAsync(ct)));

        api.MapGet("/analytics/summary", async (ClaimsPrincipal p, GeoTrackDbContext db,
            DateTime? fromUtc, DateTime? toUtc, IncidentCategory? category, IncidentStatus? status,
            IncidentPriority? priority, double? lat, double? lng, double? radiusMeters, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (fromUtc is { } from && toUtc is { } to && from >= to) errors["dateRange"] = ["fromUtc, toUtc değerinden önce olmalıdır."];
            if (lat.HasValue != lng.HasValue) errors["location"] = ["Konum filtresi için hem lat hem lng gereklidir."];
            if (radiusMeters.HasValue && !lat.HasValue) errors["location"] = ["Yarıçap için harita koordinatı girin."];
            if (lat is { } latitude && !(latitude >= -90 && latitude <= 90)) errors["lat"] = ["lat -90 ile 90 arasında olmalıdır."];
            if (lng is { } longitude && !(longitude >= -180 && longitude <= 180)) errors["lng"] = ["lng -180 ile 180 arasında olmalıdır."];
            if ((lat.HasValue || radiusMeters.HasValue) && !(radiusMeters is > 0 and <= IncidentService.MaxRadiusMeters))
                errors["radiusMeters"] = [$"radiusMeters 0'dan büyük ve en fazla {IncidentService.MaxRadiusMeters:0} olmalıdır."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var query = VisibleIncidents(p, db);
            if (fromUtc is { } start) query = query.Where(i => i.CreatedAt >= start);
            if (toUtc is { } end) query = query.Where(i => i.CreatedAt < end);
            if (category is { } cat) query = query.Where(i => i.Category == cat);
            if (status is { } state) query = query.Where(i => i.Status == state);
            if (priority is { } level) query = query.Where(i => i.Priority == level);
            if (lat is { } y && lng is { } x)
            {
                var center = GeoFactory.CreatePoint(y, x);
                query = query.Where(i => i.Location.IsWithinDistance(center, radiusMeters!.Value));
            }
            var statuses = await query.GroupBy(i => i.Status).Select(g => new { status = g.Key, count = g.Count() }).ToListAsync(ct);
            var priorities = await query.GroupBy(i => i.Priority).Select(g => new { priority = g.Key, count = g.Count() }).ToListAsync(ct);
            var categories = await query.GroupBy(i => i.Category).Select(g => new { category = g.Key, count = g.Count() }).ToListAsync(ct);
            var matrix = await query.GroupBy(i => new { i.Priority, i.Status })
                .Select(g => new { priority = g.Key.Priority, status = g.Key.Status, count = g.Count() }).ToListAsync(ct);
            var daily = await query.GroupBy(i => i.CreatedAt.Date).Select(g => new { date = g.Key, count = g.Count() })
                .OrderByDescending(x => x.date).Take(90).ToListAsync(ct);
            var timeline = daily.OrderBy(x => x.date).Select(x => new { date = x.date.ToString("yyyy-MM-dd"), x.count }).ToList();
            var resolvedCount = statuses.Where(x => x.status == IncidentStatus.Resolved).Sum(x => x.count);
            var totalCount = statuses.Sum(x => x.count);
            var highCount = priorities.Where(x => x.priority is IncidentPriority.High or IncidentPriority.Critical).Sum(x => x.count);
            return Results.Ok(new
            {
                total = totalCount,
                open = totalCount - resolvedCount,
                resolved = resolvedCount,
                highPriority = highCount,
                matrix, timeline,
                criticalOpen = await query.CountAsync(i => i.Priority == IncidentPriority.Critical && i.Status != IncidentStatus.Resolved, ct),
                statuses, priorities, categories
            });
        }).WithName("GetAnalyticsSummary");

        api.MapGet("/notifications", async (ClaimsPrincipal p, GeoTrackDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Notifications.AsNoTracking().Where(n => n.UserId == p.FindFirstValue(ClaimTypes.NameIdentifier))
                .OrderByDescending(n => n.CreatedAt).Take(100).ToListAsync(ct)));

        api.MapGet("/notifications/unread-count", async (ClaimsPrincipal p, GeoTrackDbContext db, CancellationToken ct) =>
            Results.Ok(new { count = await db.Notifications.CountAsync(n => n.UserId == p.FindFirstValue(ClaimTypes.NameIdentifier) && !n.IsRead, ct) }));

        api.MapPost("/notifications/{id:long}/read", async (long id, ClaimsPrincipal p, GeoTrackDbContext db, CancellationToken ct) =>
        {
            var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == p.FindFirstValue(ClaimTypes.NameIdentifier), ct);
            if (notification is null) return Results.NotFound();
            notification.IsRead = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        api.MapGet("/export/incidents.csv", (ClaimsPrincipal p, GeoTrackDbContext db, CancellationToken ct) =>
        {
            var query = VisibleIncidents(p, db).OrderBy(i => i.Id).AsAsyncEnumerable();
            return Results.Stream(async stream =>
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
                await writer.WriteLineAsync("id,title,description,category,status,priority,latitude,longitude,createdAtUtc".AsMemory(), ct);
                await foreach (var incident in query.WithCancellation(ct))
                {
                    var row = new StringBuilder().AppendJoin(',', incident.Id, Csv(incident.Title), Csv(incident.Description),
                        incident.Category, incident.Status, incident.Priority,
                        incident.Location.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        incident.Location.X.ToString(System.Globalization.CultureInfo.InvariantCulture), incident.CreatedAt.ToString("O"));
                    await writer.WriteLineAsync(row.ToString().AsMemory(), ct);
                }
                await writer.FlushAsync(ct);
            }, "text/csv; charset=utf-8", "incidents.csv");
        }).WithName("ExportIncidentsCsv");

        api.MapGet("/export/incidents.geojson", (ClaimsPrincipal p, GeoTrackDbContext db, CancellationToken ct) =>
        {
            var query = VisibleIncidents(p, db).OrderBy(i => i.Id).AsAsyncEnumerable();
            return Results.Stream(async stream =>
            {
                await using var writer = new Utf8JsonWriter(stream);
                writer.WriteStartObject();
                writer.WriteString("type", "FeatureCollection");
                writer.WriteStartArray("features");
                await foreach (var incident in query.WithCancellation(ct))
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "Feature");
                    writer.WriteNumber("id", incident.Id);
                    writer.WriteStartObject("geometry");
                    writer.WriteString("type", "Point");
                    writer.WriteStartArray("coordinates");
                    writer.WriteNumberValue(incident.Location.X);
                    writer.WriteNumberValue(incident.Location.Y);
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                    writer.WriteStartObject("properties");
                    writer.WriteString("title", incident.Title);
                    writer.WriteString("description", incident.Description);
                    writer.WriteString("category", incident.Category.ToString());
                    writer.WriteString("status", incident.Status.ToString());
                    writer.WriteString("priority", incident.Priority.ToString());
                    writer.WriteString("createdAt", incident.CreatedAt);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    if (writer.BytesPending > 8192) await writer.FlushAsync(ct);
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                await writer.FlushAsync(ct);
            }, "application/geo+json; charset=utf-8", "incidents.geojson");
        }).WithName("ExportIncidentsGeoJson");

        var admin = app.MapGroup("/api/admin").RequireAuthorization(p => p.RequireRole(IdentitySeeder.AdminRole));
        admin.MapGet("/overview", async (GeoTrackDbContext db, UserManager<ApplicationUser> users, CancellationToken ct) => Results.Ok(new {
                users = await users.Users.CountAsync(ct), incidents = await db.Incidents.CountAsync(ct),
                unreadNotifications = await db.Notifications.CountAsync(n => !n.IsRead, ct)
            }));
        admin.MapGet("/users", async (UserManager<ApplicationUser> users, CancellationToken ct) =>
            Results.Ok(await users.Users.AsNoTracking().OrderBy(u => u.Email)
                .Select(u => new { u.Id, u.Email, u.IsActive, u.CreatedAtUtc }).ToListAsync(ct)));
        admin.MapPut("/users/{id}/active", async (string id, SetUserActiveRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, GeoTrackDbContext db, AuditLogService audit, CancellationToken ct) =>
        {
            var target = await users.FindByIdAsync(id);
            if (target is null) return Results.NotFound();
            if (!request.IsActive && principal.FindFirstValue(ClaimTypes.NameIdentifier) == id)
                return Results.Conflict(new { message = "Kendi etkin yönetici hesabınızı kapatamazsınız." });
            if (!request.IsActive && await users.IsInRoleAsync(target, IdentitySeeder.AdminRole))
            {
                var admins = await users.GetUsersInRoleAsync(IdentitySeeder.AdminRole);
                if (admins.Count(u => u.IsActive) <= 1)
                    return Results.Conflict(new { message = "Son etkin yöneticiyi devre dışı bırakamazsınız." });
            }
            var previous = target.IsActive;
            target.IsActive = request.IsActive;
            await users.UpdateSecurityStampAsync(target);
            var saved = await users.UpdateAsync(target);
            if (!saved.Succeeded)
                return Results.ValidationProblem(saved.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray()));
            audit.Add(principal.FindFirstValue(ClaimTypes.NameIdentifier), "UserActivationChanged", "User", id,
                $"Etkinlik durumu {previous} değerinden {request.IsActive} değerine değiştirildi.");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapGet("/audit", async (int? limit, GeoTrackDbContext db, CancellationToken ct) =>
        {
            var take = Math.Clamp(limit ?? 50, 1, 200);
            return Results.Ok(await db.AuditLogs.AsNoTracking().OrderByDescending(x => x.TimestampUtc).Take(take)
                .Select(x => new { x.Id, x.TimestampUtc, x.ActorUserId, x.Action, x.EntityType, x.EntityId, x.Details }).ToListAsync(ct));
        });
        return app;
    }

    private static string Csv(string value)
    {
        // Spreadsheet formula injection guard plus RFC 4180 escaping.
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static IQueryable<Incident> VisibleIncidents(ClaimsPrincipal principal, GeoTrackDbContext db)
    {
        var query = db.Incidents.AsNoTracking().AsQueryable();
        if (!principal.IsInRole(IdentitySeeder.AdminRole))
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            query = query.Where(i => i.UserId == userId || i.UserId == null);
        }
        return query;
    }
}

public sealed record SetUserActiveRequest(bool IsActive);
