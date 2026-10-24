using GeoTrack.Web.Models;
using GeoTrack.Web.Services;

namespace GeoTrack.Tests;

/// <summary>Yeni bir enum değeri eklenip Türkçe etiketi unutulursa test başarısız olur.</summary>
public class LabelsTests
{
    [Fact]
    public void EveryCategory_HasTurkishLabel()
    {
        foreach (var value in Enum.GetValues<IncidentCategory>())
            Assert.False(string.IsNullOrWhiteSpace(value.Label()), $"Etiket eksik: {value}");
    }

    [Fact]
    public void EveryStatus_HasTurkishLabel()
    {
        foreach (var value in Enum.GetValues<IncidentStatus>())
            Assert.False(string.IsNullOrWhiteSpace(value.Label()), $"Etiket eksik: {value}");
    }

    [Fact]
    public void EveryPriority_HasTurkishLabel()
    {
        foreach (var value in Enum.GetValues<IncidentPriority>())
            Assert.False(string.IsNullOrWhiteSpace(value.Label()), $"Etiket eksik: {value}");
    }
}
