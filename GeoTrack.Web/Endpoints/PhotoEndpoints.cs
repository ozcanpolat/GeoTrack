using System.Security.Claims;
using GeoTrack.Web.Data;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services;

namespace GeoTrack.Web.Endpoints;

public static class PhotoEndpoints
{
    public static IEndpointRouteBuilder MapPhotoApi(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/incidents/{id:int}/photos").WithTags("Photos").RequireAuthorization();

        g.MapGet("/", async (int id, IncidentService svc, PhotoStore store, ClaimsPrincipal u, CancellationToken ct) =>
            await svc.GetAsync(id, ct, u.FindFirstValue(ClaimTypes.NameIdentifier), u.IsInRole("Admin")) is null
                ? Results.NotFound()
                : Results.Ok(store.List(id).Select(p => new { p.Name, p.Kind, url = $"/api/incidents/{id}/photos/{p.Name}" })))
            .WithSummary("Olayın fotoğraflarını listeler.");

        g.MapGet("/{name}", async (int id, string name, IncidentService svc, PhotoStore store, ClaimsPrincipal u, HttpContext http, CancellationToken ct) =>
        {
            if (await svc.GetAsync(id, ct, u.FindFirstValue(ClaimTypes.NameIdentifier), u.IsInRole("Admin")) is null) return Results.NotFound();
            var path = store.PathFor(id, name);
            if (path is null) return Results.NotFound();
            http.Response.Headers["Cache-Control"] = "private, max-age=3600";
            return Results.File(path, PhotoStore.ContentType(name));
        }).WithSummary("Fotoğraf dosyasını döndürür.");

        g.MapPost("/", async (int id, string kind, IFormFile file, IncidentService svc, PhotoStore store,
            GeoTrackDbContext db, AuditLogService audit, ClaimsPrincipal u, CancellationToken ct) =>
        {
            var uid = u.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = u.IsInRole(IdentitySeeder.AdminRole);
            if (!PhotoStore.IsValidKind(kind)) return Results.BadRequest(new { error = "kind: citizen veya resolution olmalı." });
            if (await svc.GetAsync(id, ct, uid, isAdmin) is null) return Results.NotFound();
            if (kind == PhotoStore.Resolution && !isAdmin) return Results.Forbid();
            var (ok, error) = await store.SaveAsync(id, kind, file, ct);
            if (!ok) return Results.BadRequest(new { error });
            audit.Add(uid, "IncidentPhotoAdded", "Incident", id.ToString(), "Olaya fotoğraf eklendi.");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/incidents/{id}/photos", new { kind });
        }).DisableAntiforgery().WithSummary("Fotoğraf yükler (multipart/form-data, alan: file; kind=citizen|resolution; JPG/PNG/WebP, en çok 5 MB).");

        g.MapDelete("/{name}", async (int id, string name, IncidentService svc, PhotoStore store,
            GeoTrackDbContext db, AuditLogService audit, ClaimsPrincipal u, CancellationToken ct) =>
        {
            var uid = u.FindFirstValue(ClaimTypes.NameIdentifier);
            var isAdmin = u.IsInRole(IdentitySeeder.AdminRole);
            if (await svc.GetAsync(id, ct, uid, isAdmin) is null) return Results.NotFound();
            if (name.StartsWith("r_") && !isAdmin) return Results.Forbid();
            if (!store.Delete(id, name)) return Results.NotFound();
            audit.Add(uid, "IncidentPhotoDeleted", "Incident", id.ToString(), "Olay fotoğrafı silindi.");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithSummary("Fotoğrafı siler (çözüm fotoğrafı yalnızca yönetici).");

        return app;
    }
}
