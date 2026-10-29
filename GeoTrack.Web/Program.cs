using System.Text.Json.Serialization;
using GeoTrack.Web.Data;
using GeoTrack.Web.Endpoints;
using GeoTrack.Web.Services;
using GeoTrack.Web.Services.AI;
using GeoTrack.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using GeoTrack.Web.Services.Email;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
var jwtKey = builder.Configuration["Jwt:SigningKey"] ?? string.Empty;
if (jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be provided through the environment and contain at least 32 characters.");
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = true;
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<GeoTrackDbContext>()
.AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(2));
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "GeoTrackSmart";
        options.DefaultChallengeScheme = "GeoTrackSmart";
        options.DefaultForbidScheme = "GeoTrackSmart";
    })
    .AddPolicyScheme("GeoTrackSmart", "Cookie or Bearer", options =>
    {
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme
                : IdentityConstants.ApplicationScheme;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "GeoTrack",
            ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"] ?? "GeoTrackClient",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.Request.Path.StartsWithSegments("/hubs/notifications"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                if (builder.Environment.IsEnvironment("Testing")) return;
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var tokenStamp = context.Principal?.FindFirst("security_stamp")?.Value;
                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = userId is null ? null : await userManager.FindByIdAsync(userId);
                if (user is null || !user.IsActive || tokenStamp is null || tokenStamp != user.SecurityStamp)
                    context.Fail("User account is inactive or its credentials have changed.");
            }
        };
    });
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser().Build());
builder.Services.AddSignalR();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

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
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddSingleton<PhotoStore>();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddHttpClient("tiles", c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("GeoTrack/1.0 (+https://github.com/geotrack; incident-map)");
});
builder.Services.AddScoped<AccountEmailService>();
builder.Services.AddScoped<EmailVerificationService>();
builder.Services.AddMemoryCache();

// AI: Ai:ApiKey VE Ai:Model ikisi de verilmişse Anthropic sağlayıcısı kullanılır; aksi halde
// "yapılandırılmadı" davranışı (CRUD/harita/API AI'dan bağımsız çalışır). Bkz. README, "AI yapılandırması".
var aiOptions = builder.Configuration.GetSection("Ai").Get<AiOptions>() ?? new AiOptions();
if (string.IsNullOrWhiteSpace(aiOptions.Model)) aiOptions.Model = AiOptions.DefaultModel;
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

// Yönetici tanılaması: eksik isteğe bağlı/zorunlu yapılandırmayı sır göstermeden logla.
{
    var startupLog = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    if (!aiOptions.IsConfigured)
        startupLog.LogWarning("AI önerisi devre dışı: AI_API_KEY (Ai__ApiKey) tanımlı değil. İsteğe bağlıdır; AI_MODEL boşsa {Model} kullanılır.", AiOptions.DefaultModel);
    var emailConfig = app.Services.CreateScope().ServiceProvider.GetRequiredService<IEmailSender>();
    if (!emailConfig.IsConfigured)
        startupLog.LogWarning("E-posta gönderimi yapılandırılmamış; kayıt kapalı. Eksik değişkenler: {Missing}. .env değiştirdiyseniz: docker compose up -d --force-recreate web", string.Join(", ", emailConfig.MissingSettings));
    else
        startupLog.LogInformation("E-posta gönderimi yapılandırıldı (sağlayıcı: {Host}).", app.Configuration["Email:SmtpHost"]);
}

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
        // TestServer testleri, dışarıdaki gerçek veritabanına bağlanmaya veya seed
        // hesabı/rolleri oluşturmaya çalışmamalı. Test verisi kullanan testler kendi
        // fixture'larını açıkça hazırlayabilir.
        if (!app.Environment.IsEnvironment("Testing"))
            await IdentitySeeder.SeedAsync(scope.ServiceProvider, app.Configuration, startupLogger);
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

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    // OSM's public tile service requires a valid Referer from web clients.
    // Sensitive account links override this with no-referrer on their own pages.
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; connect-src 'self' wss:; font-src 'self' data:";
    if (context.Request.Path.StartsWithSegments("/Account/ConfirmEmail") || context.Request.Path.StartsWithSegments("/Account/ResetPassword"))
        context.Response.Headers["Cache-Control"] = "no-store";

    var suppliedId = context.Request.Headers["X-Correlation-ID"].ToString();
    var correlationId = suppliedId.Length is > 0 and <= 64 && suppliedId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        ? suppliedId
        : context.TraceIdentifier;
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    using (context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Request").BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        await next();
});

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

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// REST API + OpenAPI belgesi (/openapi/v1.json) + Swagger UI (/swagger)
app.MapIncidentApi();
app.MapPhotoApi();
// Harita döşemeleri: tarayıcı doğrudan OSM'ye gitmez (localhost Referer'ı 403 alıyordu); sunucu uygun User-Agent ile alır ve diskte önbellekler.
app.MapGet("/tiles/{z:int:range(0,19)}/{x:int}/{y:int}.png", async (int z, int x, int y, IHttpClientFactory http, IWebHostEnvironment env, IConfiguration cfg, CancellationToken ct) =>
{
    var max = 1 << z;
    if (x < 0 || y < 0 || x >= max || y >= max) return Results.NotFound();
    var dir = Path.Combine(Path.GetTempPath(), "geotrack-tiles", z.ToString());
    var file = Path.Combine(dir, $"{x}_{y}.png");
    if (File.Exists(file) && File.GetLastWriteTimeUtc(file) > DateTime.UtcNow.AddDays(7))
        return Results.File(await File.ReadAllBytesAsync(file, ct), "image/png");
    var template = cfg["Map:TileUrl"] ?? "https://tile.openstreetmap.org/{z}/{x}/{y}.png";
    var url = template.Replace("{z}", z.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
    try
    {
        using var res = await http.CreateClient("tiles").GetAsync(url, ct);
        if (!res.IsSuccessStatusCode) return Results.StatusCode((int)res.StatusCode);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        Directory.CreateDirectory(dir);
        await File.WriteAllBytesAsync(file, bytes, ct);
        return Results.File(bytes, "image/png");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        if (File.Exists(file)) return Results.File(await File.ReadAllBytesAsync(file, ct), "image/png");
        return Results.StatusCode(502);
    }
}).ExcludeFromDescription();
app.MapGet("/healthz", async (GeoTrackDbContext db, CancellationToken ct) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return Results.Ok(new { status = "healthy" });
    }
    catch
    {
        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous().ExcludeFromDescription();
app.MapGet("/livez", () => Results.Ok(new { status = "alive" })).AllowAnonymous().ExcludeFromDescription();
app.MapOpenApi().AllowAnonymous();
app.MapGet("/swagger", () => Results.Redirect("/swagger/index.html")).AllowAnonymous().ExcludeFromDescription();
app.MapGet("/swagger/index.html", () => Results.Content("<!doctype html><html><body><h1>GeoTrack API</h1><p><a href='/openapi/v1.json'>OpenAPI JSON</a></p></body></html>", "text/html")).AllowAnonymous();
app.MapAuthApi();
app.MapWorkspaceApi();
app.MapHub<GeoTrackHub>("/hubs/notifications").RequireAuthorization();

app.Run();

// WebApplicationFactory tabanlı API testleri için.
public partial class Program { }
