using GeoTrack.Web.DTOs;
using GeoTrack.Web.Models;
using GeoTrack.Web.Services;

namespace GeoTrack.Tests;

public class IncidentInputValidationTests
{
    private static IncidentInput Valid() => new()
    {
        Title = "Mazgal tıkalı",
        Description = "Yoğun yağışta yol su altında kalıyor.",
        Category = IncidentCategory.WaterInfrastructure,
        Priority = IncidentPriority.High,
        Status = IncidentStatus.New,
        Latitude = 40.2,
        Longitude = 28.9
    };

    [Fact]
    public void ValidInput_Passes()
    {
        Assert.True(ValidationHelper.TryValidate(Valid(), out var errors));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    public void InvalidTitle_Fails(string title)
    {
        var input = Valid();
        input.Title = title;

        Assert.False(ValidationHelper.TryValidate(input, out var errors));
        Assert.Contains(nameof(IncidentInput.Title), errors.Keys);
    }

    [Fact]
    public void TooShortDescription_Fails()
    {
        var input = Valid();
        input.Description = "kısa";

        Assert.False(ValidationHelper.TryValidate(input, out var errors));
        Assert.Contains(nameof(IncidentInput.Description), errors.Keys);
    }

    [Fact]
    public void MissingCategory_Fails()
    {
        var input = Valid();
        input.Category = null;

        Assert.False(ValidationHelper.TryValidate(input, out var errors));
        Assert.Contains(nameof(IncidentInput.Category), errors.Keys);
    }

    [Theory]
    [InlineData(91)]
    [InlineData(-91)]
    public void LatitudeOutOfRange_Fails(double latitude)
    {
        var input = Valid();
        input.Latitude = latitude;

        Assert.False(ValidationHelper.TryValidate(input, out var errors));
        Assert.Contains(nameof(IncidentInput.Latitude), errors.Keys);
    }

    [Theory]
    [InlineData(181)]
    [InlineData(-181)]
    public void LongitudeOutOfRange_Fails(double longitude)
    {
        var input = Valid();
        input.Longitude = longitude;

        Assert.False(ValidationHelper.TryValidate(input, out var errors));
        Assert.Contains(nameof(IncidentInput.Longitude), errors.Keys);
    }

    [Fact]
    public void MissingCoordinates_Fail()
    {
        var input = Valid();
        input.Latitude = null;
        input.Longitude = null;

        Assert.False(ValidationHelper.TryValidate(input, out var errors));
        Assert.Contains(nameof(IncidentInput.Latitude), errors.Keys);
        Assert.Contains(nameof(IncidentInput.Longitude), errors.Keys);
    }

    [Fact]
    public void UndefinedEnumValue_Fails()
    {
        var input = Valid();
        input.Priority = (IncidentPriority)99;

        Assert.False(ValidationHelper.TryValidate(input, out var errors));
        Assert.Contains(nameof(IncidentInput.Priority), errors.Keys);
    }
}
