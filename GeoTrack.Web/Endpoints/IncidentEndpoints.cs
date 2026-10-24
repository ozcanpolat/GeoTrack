using GeoTrack.Web.DTOs;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services;
using GeoTrack.Web.Services.AI;

namespace GeoTrack.Web.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("Incidents");

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
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (page < 1) errors["page"] = ["page en az 1 olmalıdır."];
        if (pageSize < 1 || pageSize > IncidentService.MaxPageSize)
            errors["pageSize"] = [$"pageSize 1 ile {IncidentService.MaxPageSize} arasında olmalıdır."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        return Results.Ok(await svc.ListAsync(status, category, page, pageSize, ct));
    }

    private static async Task<IResult> Nearby(
        IncidentService svc, double lat, double lng, double radiusMeters = 500, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string[]>();
        if (!(lat >= -90 && lat <= 90)) errors["lat"] = ["lat -90 ile 90 arasında olmalıdır."];
        if (!(lng >= -180 && lng <= 180)) errors["lng"] = ["lng -180 ile 180 arasında olmalıdır."];
        if (!(radiusMeters > 0 && radiusMeters <= IncidentService.MaxRadiusMeters))
            errors["radiusMeters"] = [$"radiusMeters 0'dan büyük ve en fazla {IncidentService.MaxRadiusMeters:0} olmalıdır."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        return Results.Ok(await svc.NearbyAsync(lat, lng, radiusMeters, ct));
    }

    private static async Task<IResult> GetById(int id, IncidentService svc, CancellationToken ct)
    {
        var dto = await svc.GetAsync(id, ct);
        return dto is null ? Results.NotFound() : Results.Ok(dto);
    }

    private static async Task<IResult> Create(IncidentInput input, IncidentService svc, CancellationToken ct)
    {
        if (!ValidationHelper.TryValidate(input, out var errors))
            return Results.ValidationProblem(errors);

        var created = await svc.CreateAsync(input, ct);
        return Results.Created($"/api/incidents/{created.Id}", created);
    }

    private static async Task<IResult> Update(int id, IncidentInput input, IncidentService svc, CancellationToken ct)
    {
        if (!ValidationHelper.TryValidate(input, out var errors))
            return Results.ValidationProblem(errors);

        var updated = await svc.UpdateAsync(id, input, ct);
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    private static async Task<IResult> Delete(int id, IncidentService svc, CancellationToken ct)
    {
        var deleted = await svc.DeleteAsync(id, ct);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
