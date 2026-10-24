using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GeoTrack.Tests;

/// <summary>
/// API doğrulama/hata yolları. Bu isteklerin hiçbiri veritabanına ulaşmadan yanıtlanır (doğrulama servisten önce çalışır),
/// bu yüzden PostgreSQL gerekmez. 404/201 gibi veritabanı gerektiren yollar PostgisTests'te (gerçek PostGIS) kapsanır.
/// </summary>
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("ConnectionStrings:GeoTrack", "Host=localhost;Database=unused;Username=unused;Password=unused");
                b.UseSetting("Database:ApplyMigrationsOnStartup", "false");
                b.UseSetting("Database:SeedDemoData", "false");
                b.UseSetting("Ai:ApiKey", "");
                b.UseSetting("Ai:Model", "");
            })
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private const string ValidBody =
        "{\"title\":\"Mazgal tıkalı\",\"description\":\"Yoğun yağışta yol su altında kalıyor.\"," +
        "\"category\":\"WaterInfrastructure\",\"priority\":\"High\",\"status\":\"New\"," +
        "\"latitude\":40.2,\"longitude\":28.9}";

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
    public async Task OpenApiDocument_ListsIncidentEndpoints()
    {
        var res = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("/api/incidents/nearby", body);
        Assert.Contains("/api/incidents/{id}", body);
    }
}
