namespace GeoTrack.Web.Services.AI;

/// <summary>
/// AI önerisi sonucu. Kategori/öncelik değerleri IncidentCategory/IncidentPriority enum adlarıdır.
/// </summary>
public record AiSuggestion(
    string CategorySuggestion,
    string PrioritySuggestion,
    string Summary,
    string Reason);

/// <summary>
/// Sağlayıcıdan bağımsız AI servis arayüzü. Gerçek bir sağlayıcı (ör. bir LLM API'si) eklemek için
/// bu arayüzü uygulayıp Program.cs içinde NotConfiguredAiService yerine kaydedin.
/// Uygulama notları: API anahtarı yalnızca sunucuda (User Secrets / ortam değişkeni) tutulmalı,
/// yanıt yapılandırılmış JSON olarak alınıp doğrulanmalı (geçerli enum adları) ve öneri otomatik uygulanmamalıdır.
/// </summary>
public interface IIncidentAiService
{
    bool IsConfigured { get; }

    Task<AiSuggestion> SuggestAsync(string title, string description, CancellationToken ct = default);
}
