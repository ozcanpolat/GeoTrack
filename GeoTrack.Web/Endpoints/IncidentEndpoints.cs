using GeoTrack.Web.DTOs;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services;
using GeoTrack.Web.Services.AI;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace GeoTrack.Web.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("Incidents");
        group.RequireAuthorization();

        group.MapGet("/", List)
            .WithName("ListIncidents")
            .WithSummary("Arızaları sayfalı listeler (status/category filtreli).");

        // "/nearby", "/{id:int}" ile çakışmaz (id yalnızca tamsayı kabul eder).
        group.MapGet("/nearby", Nearby)
            .WithName("NearbyIncidents")
            .WithSummary("Verilen noktanın radiusMeters metre çevresindeki arızaları PostGIS ST_DWithin ile bulur.");

        group.MapGet("/{id:int}", GetById)
            .WithName("GetIncident")
            .WithSummary("Tek bir arıza kaydını getirir.");

        group.MapPost("/", Create)
            .WithName("CreateIncident")
            .WithSummary("Yeni arıza kaydı oluşturur.");

        group.MapPut("/{id:int}", Update)
            .WithName("UpdateIncident")
            .WithSummary("Var olan arıza kaydını günceller.");

        group.MapDelete("/{id:int}", Delete)
            .WithName("DeleteIncident")
            .WithSummary("Arıza kaydını siler.");

        group.MapPost("/{id:int}/workflow", UpdateWorkflow)
            .WithName("UpdateIncidentWorkflow")
            .WithSummary("Durum geçişi ve yönetici ataması uygular.");
        group.MapGet("/{id:int}/history", GetHistory)
            .WithName("GetIncidentHistory")
            .WithSummary("Yetkili olunan olayın değişiklik geçmişini döndürür.");

        group.MapPost("/ai-suggestion", Suggest)
            .WithName("SuggestIncidentClassification")
            .WithSummary("Başlık/açıklamadan kategori, öncelik ve özet önerir. AI yapılandırılmadıysa 503 döner. Öneri kaydedilmez.");

        return app;
    }

    private static async Task<IResult> Suggest(
        AiSuggestionRequest request, IIncidentAiService ai, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        if (!ValidationHelper.TryValidate(request, out var errors))
            return Results.ValidationProblem(errors);

        if (!ai.IsConfigured)
        {
            return Results.Problem(
                title: "AI entegrasyonu yapılandırılmadı",
                detail: "Bu özellik için bir AI sağlayıcısı yapılandırılmamış. Diğer tüm özellikler çalışmaya devam eder.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            var suggestion = await ai.SuggestAsync(request.Title, request.Description, ct);

            // Sağlayıcı yanıtı doğrulanır: yalnızca bilinen enum adları kabul edilir.
            if (!Enum.TryParse<IncidentCategory>(suggestion.CategorySuggestion, out _) ||
                !Enum.TryParse<IncidentPriority>(suggestion.PrioritySuggestion, out _))
            {
                throw new InvalidOperationException("AI yanıtı geçersiz kategori/öncelik içeriyor.");
            }

            return Results.Ok(suggestion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            loggerFactory.CreateLogger("IncidentEndpoints").LogError(ex, "AI önerisi alınamadı.");
            return Results.Problem(
                title: "AI servisi şu an kullanılamıyor",
                detail: "Öneri alınamadı. Arıza kaydını önerisiz oluşturabilirsiniz.",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> List(
        IncidentService svc,
        IncidentStatus? status,
        IncidentCategory? category,
        IncidentPriority? priority,
        ClaimsPrincipal user,
        int page = 1,
        int pageSize = 20,
        string? q = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["page en az 1 olmalıdır."];
        if (pageSize < 1 || pageSize > IncidentService.MaxPageSize)
            errors["pageSize"] = [$"pageSize 1 ile {IncidentService.MaxPageSize} arasında olmalıdır."];
        if (q?.Length > 150) errors["q"] = ["Arama en fazla 150 karakter olabilir."];
        if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value >= toUtc.Value)
            errors["dateRange"] = ["fromUtc, toUtc değerinden önce olmalıdır."];
        if (sort is not null && !new[] { "recent", "oldest", "priority", "title" }.Contains(sort, StringComparer.OrdinalIgnoreCase))
            errors["sort"] = ["sort recent, oldest, priority veya title olabilir."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        return Results.Ok(await svc.ListAsync(status, category, page, pageSize, ct,
            user.FindFirstValue(ClaimTypes.NameIdentifier), user.IsInRole("Admin"), priority, q, fromUtc, toUtc, sort));
    }

    private static async Task<IResult> Nearby(
        IncidentService svc, ClaimsPrincipal user, double lat, double lng, double radiusMeters = 500, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (!(lat >= -90 && lat <= 90)) errors["lat"] = ["lat -90 ile 90 arasında olmalıdır."];
        if (!(lng >= -180 && lng <= 180)) errors["lng"] = ["lng -180 ile 180 arasında olmalıdır."];
        if (!(radiusMeters > 0 && radiusMeters <= IncidentService.MaxRadiusMeters))
            errors["radiusMeters"] = [$"radiusMeters 0'dan büyük ve en fazla {IncidentService.MaxRadiusMeters:0} olmalıdır."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        return Results.Ok(await svc.NearbyAsync(lat, lng, radiusMeters, ct, user.FindFirstValue(ClaimTypes.NameIdentifier), user.IsInRole("Admin")));
    }

    private static async Task<IResult> GetById(int id, IncidentService svc, ClaimsPrincipal user, CancellationToken ct)
    {
        var dto = await svc.GetAsync(id, ct, user.FindFirstValue(ClaimTypes.NameIdentifier), user.IsInRole("Admin"));
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> Create(IncidentInput input, IncidentService svc, ClaimsPrincipal user, IHubContext<GeoTrackHub> hub, GeoTrackDbContext db, AuditLogService audit, CancellationToken ct)
    {
        if (!ValidationHelper.TryValidate(input, out var errors))
            return Results.ValidationProblem(errors);
        if (input.Status != IncidentStatus.New)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Yeni olaylar New durumunda oluşturulur; sonraki durumları iş akışından seçin."] });

        var created = await svc.CreateAsync(input, ct, user.FindFirstValue(ClaimTypes.NameIdentifier));
        var owner = user.FindFirstValue(ClaimTypes.NameIdentifier);
        audit.Add(owner, "IncidentCreated", "Incident", created.Id.ToString(), "Olay kaydı oluşturuldu.");
        if (owner is not null)
        {
            db.Notifications.Add(new UserNotification { UserId = owner, IncidentId = created.Id, Message = $"Olay kaydı oluşturuldu: {created.Title}" });
        }
        await db.SaveChangesAsync(ct);
        if (owner is not null)
        {
            await hub.Clients.Group($"user:{owner}").SendAsync("notification", new { message = "Olay kaydınız oluşturuldu.", incidentId = created.Id }, ct);
        }
        return Results.Created($"/api/incidents/{created.Id}", created);
    }

    private static async Task<IResult> Update(int id, IncidentInput input, IncidentService svc, ClaimsPrincipal user,
        GeoTrackDbContext db, IHubContext<GeoTrackHub> hub, AuditLogService audit, CancellationToken ct)
    {
        if (!ValidationHelper.TryValidate(input, out var errors))
            return Results.ValidationProblem(errors);

        IncidentDto? updated;
        try
        {
            updated = await svc.UpdateAsync(id, input, ct, user.FindFirstValue(ClaimTypes.NameIdentifier), user.IsInRole("Admin"));
        }
        catch (InvalidIncidentTransitionException ex) { return Results.Conflict(new { message = ex.Message }); }
        catch (IncidentAssignmentRequiredException ex) { return Results.Conflict(new { message = ex.Message }); }
        if (updated is not null)
        {
            var ownerId = await db.Incidents.AsNoTracking().Where(i => i.Id == id).Select(i => i.UserId).FirstOrDefaultAsync(ct);
            audit.Add(user.FindFirstValue(ClaimTypes.NameIdentifier), "IncidentUpdated", "Incident", id.ToString(), "Olay bilgileri güncellendi.");
            if (ownerId is not null)
            {
                db.Notifications.Add(new UserNotification { UserId = ownerId, IncidentId = id, Message = $"Olay kaydı güncellendi: {updated.Title}" });
            }
            await db.SaveChangesAsync(ct);
            if (ownerId is not null)
            {
                await hub.Clients.Group($"user:{ownerId}").SendAsync("notification", new { message = "Olay kaydınız güncellendi.", incidentId = id }, ct);
            }
        }
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static async Task<IResult> Delete(int id, IncidentService svc, ClaimsPrincipal user, GeoTrackDbContext db,
        AuditLogService audit, IHubContext<GeoTrackHub> hub, PhotoStore photos, CancellationToken ct)
    {
        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var ownerId = await db.Incidents.AsNoTracking().Where(i => i.Id == id).Select(i => i.UserId).FirstOrDefaultAsync(ct);
        var deleted = await svc.DeleteAsync(id, ct, actorId, user.IsInRole("Admin"));
        if (deleted)
        {
            photos.DeleteAll(id);
            audit.Add(actorId, "IncidentDeleted", "Incident", id.ToString(), "Olay kaydı silindi.");
            if (ownerId is not null && ownerId != actorId)
                db.Notifications.Add(new UserNotification { UserId = ownerId, IncidentId = null, Message = $"#{id} numaralı olay bir yönetici tarafından kaldırıldı." });
            await db.SaveChangesAsync(ct);
            if (ownerId is not null && ownerId != actorId)
                await hub.Clients.Group($"user:{ownerId}").SendAsync("notification", new { message = "Bir olay kaydınız yönetici tarafından kaldırıldı.", incidentId = (int?)null }, ct);
        }
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> UpdateWorkflow(int id, IncidentWorkflowRequest request, IncidentService svc,
        ClaimsPrincipal user, UserManager<ApplicationUser> users, GeoTrackDbContext db, AuditLogService audit,
        IHubContext<GeoTrackHub> hub, ILoggerFactory loggerFactory, CancellationToken ct)
    {
        var isAdmin = user.IsInRole(IdentitySeeder.AdminRole);
        if (!isAdmin && request.AssignedToUserId is not null) return Results.Forbid();
        var assignedToUserId = string.IsNullOrWhiteSpace(request.AssignedToUserId) ? null : request.AssignedToUserId.Trim();
        if (isAdmin && assignedToUserId is { Length: > 0 } targetId)
        {
            var target = await users.FindByIdAsync(targetId);
            var hasAssignableRole = target is not null &&
                (await users.IsInRoleAsync(target, IdentitySeeder.UserRole) || await users.IsInRoleAsync(target, IdentitySeeder.AdminRole));
            if (target is null || !target.IsActive || !hasAssignableRole)
            {
                loggerFactory.CreateLogger("IncidentEndpoints").LogWarning("Workflow assignment rejected because target account is missing, inactive, or has no assignable role.");
                return Results.BadRequest(new { message = "Atanacak kullanıcı bulunamadı veya etkin değil." });
            }
        }

        var actorId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        IncidentDto? updated;
        try
        {
            updated = await svc.ApplyWorkflowAsync(id, request.Status, assignedToUserId,
                isAdmin && request.AssignmentProvided, isAdmin, actorId, ct);
        }
        catch (InvalidIncidentTransitionException ex) { return Results.Conflict(new { message = ex.Message }); }
        catch (IncidentAssignmentRequiredException ex) { return Results.Conflict(new { message = ex.Message }); }
        catch (IncidentAssignmentForbiddenException) { return Results.Forbid(); }
        if (updated is null) return Results.NotFound();

        audit.Add(actorId, "IncidentWorkflowUpdated", "Incident", id.ToString(), "Olay durumu veya ataması güncellendi.");
        var ownerId = await db.Incidents.AsNoTracking().Where(i => i.Id == id).Select(i => i.UserId).FirstOrDefaultAsync(ct);
        if (ownerId is not null)
            db.Notifications.Add(new UserNotification { UserId = ownerId, IncidentId = id, Message = $"Olay durumu güncellendi: {updated.Title} · {updated.Status}" });
        await db.SaveChangesAsync(ct);
        if (ownerId is not null)
            await hub.Clients.Group($"user:{ownerId}").SendAsync("notification", new { message = "Olay durumunuz güncellendi.", incidentId = id }, ct);
        return Results.Ok(updated);
    }

    private static async Task<IResult> GetHistory(int id, IncidentService svc, ClaimsPrincipal user,
        GeoTrackDbContext db, CancellationToken ct)
    {
        var visible = await svc.GetAsync(id, ct, user.FindFirstValue(ClaimTypes.NameIdentifier), user.IsInRole(IdentitySeeder.AdminRole));
        if (visible is null) return Results.NotFound();
        var history = await db.IncidentHistory.AsNoTracking().Where(h => h.IncidentId == id)
            .OrderByDescending(h => h.CreatedAtUtc).Take(100)
            .Select(h => new { h.Id, h.Action, h.PreviousValue, h.NewValue, h.CreatedAtUtc }).ToListAsync(ct);
        return Results.Ok(history);
    }
}

public sealed record IncidentWorkflowRequest(IncidentStatus Status, string? AssignedToUserId = null,
    bool AssignmentProvided = true);
