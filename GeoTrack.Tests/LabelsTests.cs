using GeoTrack.Web.Models;
using GeoTrack.Web.Services;

namespace GeoTrack.Tests;

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
