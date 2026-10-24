using GeoTrack.Web.Services.AI;

namespace GeoTrack.Tests;

public class AiResponseParserTests
{
    private const string Valid =
        "{\"categorySuggestion\":\"Electricity\",\"prioritySuggestion\":\"Critical\"," +
        "\"summary\":\"Açık trafo kapağı.\",\"reason\":\"Güvenlik riski.\"}";

    [Fact]
    public void ValidJson_IsParsed()
    {
        var s = AiResponseParser.Parse(Valid);

        Assert.Equal("Electricity", s.CategorySuggestion);
        Assert.Equal("Critical", s.PrioritySuggestion);
        Assert.Equal("Açık trafo kapağı.", s.Summary);
        Assert.Equal("Güvenlik riski.", s.Reason);
    }

    [Fact]
    public void JsonInsideCodeFenceAndText_IsParsed()
    {
        var s = AiResponseParser.Parse("İşte sonuç:\n```json\n" + Valid + "\n```\nBaşka bir şey.");
        Assert.Equal("Electricity", s.CategorySuggestion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("JSON yok")]
    [InlineData("{ bozuk json")]
    [InlineData("{\"categorySuggestion\":\"Electricity\"}")]
    [InlineData("{\"categorySuggestion\":\"Uzay\",\"prioritySuggestion\":\"Low\",\"summary\":\"x\",\"reason\":\"y\"}")]
    [InlineData("{\"categorySuggestion\":\"Road\",\"prioritySuggestion\":\"Acil\",\"summary\":\"x\",\"reason\":\"y\"}")]
    [InlineData("{\"categorySuggestion\":\"99\",\"prioritySuggestion\":\"Low\",\"summary\":\"x\",\"reason\":\"y\"}")]
    [InlineData("{\"categorySuggestion\":5,\"prioritySuggestion\":\"Low\",\"summary\":\"x\",\"reason\":\"y\"}")]
    public void InvalidResponse_ThrowsFormatException(string? text)
    {
        Assert.Throws<FormatException>(() => AiResponseParser.Parse(text));
    }

    [Fact]
    public void LongTexts_AreTrimmed()
    {
        var json = "{\"categorySuggestion\":\"Road\",\"prioritySuggestion\":\"Low\",\"summary\":\"" +
                   new string('a', 1000) + "\",\"reason\":\"" + new string('b', 1000) + "\"}";

        var s = AiResponseParser.Parse(json);

        Assert.Equal(300, s.Summary.Length);
        Assert.Equal(500, s.Reason.Length);
    }
}
