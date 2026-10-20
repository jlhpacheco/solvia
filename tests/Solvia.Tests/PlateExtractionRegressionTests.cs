using Solvia.Services;

namespace Solvia.Tests;

public sealed partial class ExtractionTests
{
    [Theory]
    [InlineData("I have a volvo 760 white color and license plate 999111", "999111")]
    [InlineData("License Plate Number: 999111", "999111")]
    [InlineData("LICENSE PLATE # ABC-123", "ABC123")]
    [InlineData("plate is 8xyz123 for fall", "8XYZ123")]
    [InlineData("license plate 8XYZ 123, CA", "8XYZ123")]
    [InlineData("plates ABC 123 in California", "ABC123")]
    [InlineData("licence plate no. ABC1234", "ABC1234")]
    [InlineData("plate: PARKME", "PARKME")]
    [InlineData("registration number 123456", "123456")]
    [InlineData("plate 999111 red Volvo 760", "999111")]
    [InlineData("plate ABC123 BMW sedan", "ABC123")]
    [InlineData("plate AB 12 CD", "AB12CD")]
    [InlineData("I may need a day pass; plate ABC123", "ABC123")]
    [InlineData("plate ABC123; I could buy a term permit", "ABC123")]
    [InlineData("Maybe I need a day pass; plate ABC123", "ABC123")]
    [InlineData("My plate is ABC123, and my car may be blue", "ABC123")]
    public void CompoundPlateLabels_KeepOnlyThePlate(string sentence, string expected)
    {
        Assert.Equal(expected, new FallbackExtractor(Catalog).Extract(sentence).Plate);
    }

    [Theory]
    [InlineData("My license plate is unknown")]
    [InlineData("I don't have a license plate yet")]
    [InlineData("license plate number")]
    [InlineData("plate ABC123 and plate XYZ987")]
    [InlineData("plate ABC123 or XYZ987")]
    [InlineData("plate ABC123 and XYZ987")]
    [InlineData("plate ABC123, XYZ987")]
    [InlineData("plate ABC123 / XYZ987")]
    [InlineData("8XYZ123 or XYZ987")]
    [InlineData("ABC123 or 8XYZ123")]
    [InlineData("I am not sure, plate ABC123")]
    [InlineData("white Volvo 760; plate might be 234221 or 234222")]
    [InlineData("plate may be ABC123 or XYZ987")]
    [InlineData("plate could be ABC123")]
    [InlineData("plate is either ABC123 or XYZ987")]
    [InlineData("either plate ABC123 or XYZ987")]
    [InlineData("could be my plate ABC123")]
    [InlineData("I think my license plate is ABC123")]
    [InlineData("plate probably ABC123")]
    [InlineData("plate seems to be ABC123")]
    [InlineData("plate MIGHT")]
    [InlineData("plate COULD")]
    public void MissingOrConflictingPlate_AsksForConfirmation(string sentence)
    {
        Assert.Null(new FallbackExtractor(Catalog).Extract(sentence).Plate);
    }

    [Fact]
    public async Task ModelOmitsNumericPlate_ExplicitInputSurvives()
    {
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope("{\"carMake\":\"Volvo\",\"carModel\":\"760\",\"carColor\":\"White\"}") };
        var result = await NewService(handler).ExtractAsync("white Volvo 760, License plate number 999111");
        Assert.Equal("999111", result.Plate);
        Assert.Equal(ExtractionSource.Ollama, result.Source);
    }

    [Theory]
    [InlineData("white Volvo 760, license plate 999111", "8XYZ123", "999111")]
    [InlineData("plate ABC-123", "XYZ987", "ABC123")]
    [InlineData("8xyz123", "9ABC456", "8XYZ123")]
    [InlineData("my vehicle has registration number 123456", null, "123456")]
    [InlineData("white Volvo 760", "8XYZ123", null)]
    [InlineData("my license plate is unknown", "8XYZ123", null)]
    [InlineData("I don't have a license plate yet", "8XYZ123", null)]
    [InlineData("plate ABC123 and plate XYZ987", "ABC123", null)]
    [InlineData("plate ABC123 or XYZ987", "ABC123", null)]
    [InlineData("maybe plate ABC123", "ABC123", null)]
    [InlineData("I don't know my plate ABC123", "ABC123", null)]
    [InlineData("ABC123 or XYZ987", "ABC123", null)]
    [InlineData("ABC123 or XYZ987", "XYZ987", null)]
    [InlineData("blue Honda Civic, ABC-123", "ABC123", "ABC123")]
    [InlineData("blue Honda Civic, 999 111", "999111", "999111")]
    [InlineData("plate might be 234221 or 234222", "234221", null)]
    [InlineData("plate may be ABC123", "ABC123", null)]
    [InlineData("either ABC123 or XYZ987", "XYZ987", null)]
    [InlineData("Maybe I need a day pass; plate ABC123", "XYZ987", "ABC123")]
    public async Task ModelPlate_IsGroundedInExplicitUnambiguousInput(
        string sentence, string? modelPlate, string? expected)
    {
        var content = System.Text.Json.JsonSerializer.Serialize(new { plate = modelPlate });
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope(content) };

        var result = await NewService(handler).ExtractAsync(sentence);

        Assert.Equal(expected, result.Plate);
        Assert.Equal(ExtractionSource.Ollama, result.Source);
    }

    [Theory]
    [InlineData("99", "0", null, null)]
    [InlineData("04", "999", null, null)]
    [InlineData("CA", "Black, White", "CA", null)]
    [InlineData("ca", "bLuE", "CA", "Blue")]
    [InlineData("NV", "grey", "NV", "Gray")]
    [InlineData("ZZ", "Beige", null, null)]
    public async Task ModelStateAndColor_RequireNamedVocabulary(
        string stateClaim, string colorClaim, string? expectedState, string? expectedColor)
    {
        var content = System.Text.Json.JsonSerializer.Serialize(new { carState = stateClaim, carColor = colorClaim });
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope(content) };

        var result = await NewService(handler).ExtractAsync("a fictional vehicle");

        Assert.Equal(expectedState, result.CarState);
        Assert.Equal(expectedColor, result.CarColor);
        Assert.Equal(ExtractionSource.Ollama, result.Source);
    }
}
