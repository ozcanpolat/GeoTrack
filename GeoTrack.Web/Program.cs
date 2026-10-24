using System.Text.Json.Serialization;
using GeoTrack.Web.Data;
using GeoTrack.Web.Endpoints;
using GeoTrack.Web.Services;
using GeoTrack.Web.Services.AI;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// API'de enum değerleri metin olarak (ör. "InProgress") okunur/yazılır.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("GeoTrack");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "ConnectionStrings:GeoTrack tanımlı değil. Geliştirmede appsettings.Development.json, " +
        "başka ortamlarda ConnectionStrings__GeoTrack ortam değişkeni kullanın.");
}

builder.Services.AddDbContext<GeoTrackDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));
builder.Services.AddScoped<IncidentService>();

// AI: Ai:ApiKey VE Ai:Model ikisi de verilmişse Anthropic sağlayıcısı kullanılır; aksi halde
// "yapılandırılmadı" davranışı (CRUD/harita/API AI'dan bağımsız çalışır). Bkz. README, "AI yapılandırması".
var aiOptions = builder.Configuration.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
if (aiOptions.IsConfigured)
{
    builder.Services.AddSingleton(aiOptions);
    builder.Services.AddHttpClient<IIncidentAiService, AnthropicAiService>(c => c.Timeout = TimeSpan.FromSeconds(30));
}
else
{
    builder.Services.AddSingleton<IIncidentAiService, NotConfiguredAiService>();
}

var app = builder.Build();

// İsteğe bağlı: açılışta migration uygula ve demo veri ekle (yapılandırmayla kontrol edilir).
// Veritabanı kapalıysa uygulama çökmez; hata loglanır ve sayfalar anlaşılır hata gösterir.
using (var scope = app.Services.CreateScope())
{
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<GeoTrackDbContext>();
        if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            await db.Database.MigrateAsync();
        }
        if (app.Configuration.GetValue<bool>("Database:SeedDemoData"))
        {
            await DemoDataSeeder.SeedAsync(db);
        }
    }
    catch (Exception ex)
    {
        startupLogger.LogError(ex, "Açılışta veritabanı hazırlanamadı. PostgreSQL/PostGIS çalışıyor ve migration uygulanmış mı?");
    }
}

// Ondalık ayırıcı sorunlarını önlemek için sunucu kültürü sabit (en-US).
// Tarihler arayüzde elle "dd.MM.yyyy HH:mm" biçiminde gösterilir.
app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("en-US")
    .AddSupportedCultures("en-US")
    .AddSupportedUICultures("en-US"));

// Hata ayrıntıları (stack trace) kullanıcıya gösterilmez; yalnızca loglanır.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

    // Geçersiz JSON / bilinmeyen enum değeri gibi istemci hataları 500 değil 400 olmalıdır.
    if (feature?.Error is BadHttpRequestException badRequest)
    {
        logger.LogWarning(badRequest, "Geçersiz istek: {Path}", context.Request.Path);
        context.Response.StatusCode = badRequest.StatusCode;
        await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = badRequest.StatusCode,
            Title = "Geçersiz istek",
            Detail = "İstek okunamadı. JSON biçimini, alan adlarını ve enum değerlerini kontrol edin."
        });
        return;
    }

    logger.LogError(feature?.Error, "Yakalanmamış hata: {Path}", context.Request.Path);

    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 500,
            Title = "Sunucu hatası",
            Detail = "İşlem tamamlanamadı. Lütfen daha sonra tekrar deneyin."
        });
    }
    else
    {
        context.Response.Redirect("/Error");
    }
}));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();

// REST API + OpenAPI belgesi (/openapi/v1.json) + Swagger UI (/swagger)
app.MapIncidentApi();
app.MapOpenApi();
app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html")).ExcludeFromDescription();

app.Run();

// WebApplicationFactory tabanlı API testleri için.
public partial class Program { }
