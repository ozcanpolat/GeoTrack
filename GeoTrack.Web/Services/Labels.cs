using GeoTrack.Web.Models;

namespace GeoTrack.Web.Services;

/// <summary>Enum değerlerinin Türkçe arayüz etiketleri.</summary>
public static class Labels
{
    public static readonly IReadOnlyDictionary<IncidentCategory, string> Categories =
        new Dictionary<IncidentCategory, string>
        {
            [IncidentCategory.Road] = "Yol",
            [IncidentCategory.WaterInfrastructure] = "Su Altyapısı",
            [IncidentCategory.Electricity] = "Elektrik",
            [IncidentCategory.Waste] = "Atık",
            [IncidentCategory.Other] = "Diğer"
        };

    public static readonly IReadOnlyDictionary<IncidentStatus, string> Statuses =
        new Dictionary<IncidentStatus, string>
        {
            [IncidentStatus.New] = "Yeni",
            [IncidentStatus.InProgress] = "Devam Ediyor",
            [IncidentStatus.Assigned] = "Atandı",
            [IncidentStatus.Resolved] = "Çözüldü"
        };

    public static readonly IReadOnlyDictionary<IncidentPriority, string> Priorities =
        new Dictionary<IncidentPriority, string>
        {
            [IncidentPriority.Low] = "Düşük",
            [IncidentPriority.Medium] = "Orta",
            [IncidentPriority.High] = "Yüksek",
            [IncidentPriority.Critical] = "Kritik"
        };

    public static string Label(this IncidentCategory value) => Categories[value];
    public static string Label(this IncidentStatus value) => Statuses[value];
    public static string Label(this IncidentPriority value) => Priorities[value];

    /// <summary>Tarihleri sunucu kültüründen bağımsız, Türkçe alışkanlığa uygun gösterir.</summary>
    public static string FormatDate(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
