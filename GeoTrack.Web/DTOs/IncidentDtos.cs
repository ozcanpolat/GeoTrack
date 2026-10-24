using System.ComponentModel.DataAnnotations;
using GeoTrack.Web.Models;

namespace GeoTrack.Web.DTOs;

/// <summary>Oluşturma ve güncelleme için ortak giriş modeli (Razor formu ve REST API).</summary>
public class IncidentInput
{
    [Required(ErrorMessage = "Başlık zorunludur.")]
    [StringLength(150, MinimumLength = 3, ErrorMessage = "Başlık 3 ile 150 karakter arasında olmalıdır.")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Açıklama zorunludur.")]
    [StringLength(2000, MinimumLength = 10, ErrorMessage = "Açıklama 10 ile 2000 karakter arasında olmalıdır.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kategori seçiniz.")]
    [EnumDataType(typeof(IncidentCategory), ErrorMessage = "Geçersiz kategori.")]
    public IncidentCategory? Category { get; set; }

    [EnumDataType(typeof(IncidentStatus), ErrorMessage = "Geçersiz durum.")]
    public IncidentStatus Status { get; set; } = IncidentStatus.New;

    [EnumDataType(typeof(IncidentPriority), ErrorMessage = "Geçersiz öncelik.")]
    public IncidentPriority Priority { get; set; } = IncidentPriority.Medium;

    [Required(ErrorMessage = "Enlem zorunludur.")]
    [Range(-90, 90, ErrorMessage = "Enlem -90 ile 90 arasında olmalıdır.")]
    public double? Latitude { get; set; }

    [Required(ErrorMessage = "Boylam zorunludur.")]
    [Range(-180, 180, ErrorMessage = "Boylam -180 ile 180 arasında olmalıdır.")]
    public double? Longitude { get; set; }
}

public record IncidentDto(
    int Id,
    string Title,
    string Description,
    IncidentCategory Category,
    IncidentStatus Status,
    IncidentPriority Priority,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    double Latitude,
    double Longitude,
    double? DistanceMeters = null);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record StatsDto(
    int Total,
    int New,
    int InProgress,
    int Assigned,
    int Resolved,
    int CriticalOpen);

/// <summary>AI öneri isteği (yalnızca metin gönderilir).</summary>
public class AiSuggestionRequest
{
    [Required(ErrorMessage = "Başlık zorunludur.")]
    [StringLength(150, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Açıklama zorunludur.")]
    [StringLength(2000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;
}
