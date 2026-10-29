using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace GeoTrack.Tests;

public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestKey = "tests-only-signing-key-at-least-32-chars";
    private readonly HttpClient _client;
    private readonly HttpClient _anonymousClient;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        var configuredFactory = factory
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Testing");
                b.UseSetting("ConnectionStrings:GeoTrack", "Host=localhost;Database=unused;Username=unused;Password=unused");
                b.UseSetting("Database:ApplyMigrationsOnStartup", "false");
                b.UseSetting("Database:SeedDemoData", "false");
                b.UseSetting("Ai:ApiKey", "");
                b.UseSetting("Ai:Model", "");
                b.UseSetting("Jwt:SigningKey", TestKey);
            });
        _client = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        _anonymousClient = configuredFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: "GeoTrack", audience: "GeoTrackClient",
            claims: [new Claim(ClaimTypes.NameIdentifier, "test-user"), new Claim(ClaimTypes.Role, "Admin")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestKey)), SecurityAlgorithms.HmacSha256)));
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private const string ValidBody =
        "{\"title\":\"Mazgal tıkalı\",\"description\":\"Yoğun yağışta yol su altında kalıyor.\"," +
        "\"category\":\"WaterInfrastructure\",\"priority\":\"High\",\"status\":\"New\"," +
        "\"latitude\":40.2,\"longitude\":28.9}";

    [Fact]
    public async Task IncidentsApi_RequiresAuthentication()
    {
        var res = await _anonymousClient.GetAsync("/api/incidents/");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidBody_Returns400WithFieldErrors()
    {
        var res = await _client.PostAsync("/api/incidents", Json("{\"title\":\"ab\",\"latitude\":95}"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("errors", body);
    }

    [Fact]
    public async Task Post_MalformedJson_Returns400_Not500()
    {
        var res = await _client.PostAsync("/api/incidents", Json("{ bozuk"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Post_UnknownEnumValue_Returns400_Not500()
    {
        var res = await _client.PostAsync("/api/incidents", Json(ValidBody.Replace("WaterInfrastructure", "Uzay")));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Put_InvalidBody_Returns400()
    {
        var res = await _client.PutAsync("/api/incidents/1", Json("{\"title\":\"\"}"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("lat=95&lng=28.9&radiusMeters=500")]
    [InlineData("lat=40.2&lng=181&radiusMeters=500")]
    [InlineData("lat=40.2&lng=28.9&radiusMeters=-5")]
    [InlineData("lat=40.2&lng=28.9&radiusMeters=0")]
    [InlineData("lat=40.2&lng=28.9&radiusMeters=999999999")]
    [InlineData("lat=NaN&lng=28.9&radiusMeters=500")]
    public async Task Nearby_InvalidParameters_Returns400(string query)
    {
        var res = await _client.GetAsync("/api/incidents/nearby?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_InvalidPaging_Returns400(string query)
    {
        var res = await _client.GetAsync("/api/incidents?" + query);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("sort=random")]
    [InlineData("fromUtc=2025-02-01T00%3A00%3A00Z&toUtc=2025-01-01T00%3A00%3A00Z")]
    [InlineData("q=12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890")]
    public async Task List_InvalidFilters_Return400(string query)
    {
        var res = await _client.GetAsync("/api/incidents?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task AiSuggestion_WhenNotConfigured_Returns503()
    {
        var res = await _client.PostAsJsonAsync("/api/incidents/ai-suggestion", new
        {
            title = "Mazgal tıkalı",
            description = "Yoğun yağışta yol su altında kalıyor."
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task AiSuggestion_InvalidBody_Returns400()
    {
        var res = await _client.PostAsJsonAsync("/api/incidents/ai-suggestion", new { title = "a", description = "b" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Register_WhenEmailIsNotConfigured_Returns503BeforeCreatingAnAccount()
    {
        var res = await _anonymousClient.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"mail-disabled-{Guid.NewGuid():N}@example.test",
            password = "ValidPassword123!"
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("SMTP_", body); // teknik yapılandırma ayrıntısı kullanıcıya gösterilmez
    }

    [Fact]
    public async Task RegistrationPage_AllowsTileProviderToReceiveOriginReferrer()
    {
        var res = await _anonymousClient.GetAsync("/Account/Register");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("strict-origin-when-cross-origin", Assert.Single(res.Headers.GetValues("Referrer-Policy")));
    }

    [Fact]
    public async Task NotificationsPage_RendersWithLocalSignalRClient()
    {
        var res = await _client.GetAsync("/Notifications");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("notification-list", body);
        Assert.Contains("/vendor/signalr/signalr.min.js", body);
    }

    [Theory]
    [InlineData("/Search", "Olaylarda ara")]
    [InlineData("/Analytics", "Operasyon analitiği")]
    public async Task WorkspacePages_RenderConcretePageModels(string path, string heading)
    {
        var res = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(heading, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OpenApiDocument_ListsIncidentEndpoints()
    {
        var res = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("/api/incidents/nearby", body);
        Assert.Contains("/api/incidents/{id}", body);
    }
}
