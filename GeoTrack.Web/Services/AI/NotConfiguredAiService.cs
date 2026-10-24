namespace GeoTrack.Web.Services.AI;

/// <summary>
/// Varsayılan kayıt: gerçek bir AI sağlayıcısı yapılandırılmamıştır. Bu bir AI değildir;
/// kural tabanlı sahte öneri üretmez. CRUD/harita/API özellikleri AI olmadan tam çalışır.
/// </summary>
public sealed class NotConfiguredAiService : IIncidentAiService
{
    public bool IsConfigured => false;

    public Task<AiSuggestion> SuggestAsync(string title, string description, CancellationToken ct = default) =>
        throw new InvalidOperationException("AI entegrasyonu yapılandırılmadı.");
}
