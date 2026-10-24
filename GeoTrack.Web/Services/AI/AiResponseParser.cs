using System.Text.Json;
using GeoTrack.Web.Models;

namespace GeoTrack.Web.Services.AI;

/// <summary>
/// Model çıktısını (JSON metni) ayrıştırır ve doğrular. Modeller JSON'u bazen ```json çitiyle
/// veya öncesinde/sonrasında metinle verir; ilk '{' ile son '}' arası alınır.
/// Geçersiz kategori/öncelik adları reddedilir (FormatException).
/// </summary>
public static class AiResponseParser
{
    public static AiSuggestion Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new FormatException("AI yanıtı boş.");

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new FormatException("AI yanıtında JSON bulunamadı.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text[start..(end + 1)]);
        }
        catch (JsonException ex)
        {
            throw new FormatException("AI yanıtı geçerli JSON değil.", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            string Get(string name) =>
                root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                    ? p.GetString()!.Trim()
                    : throw new FormatException($"AI yanıtında '{name}' alanı eksik.");

            var category = Get("categorySuggestion");
            var priority = Get("prioritySuggestion");
            var summary = Get("summary");
            var reason = Get("reason");

            if (!Enum.TryParse<IncidentCategory>(category, out var c) || !Enum.IsDefined(c))
                throw new FormatException($"Geçersiz kategori önerisi: {category}");
            if (!Enum.TryParse<IncidentPriority>(priority, out var p2) || !Enum.IsDefined(p2))
                throw new FormatException($"Geçersiz öncelik önerisi: {priority}");

            // Arayüze taşınan metinlerin makul uzunlukta kalması için kırpılır.
            return new AiSuggestion(c.ToString(), p2.ToString(), Trim(summary, 300), Trim(reason, 500));
        }
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
