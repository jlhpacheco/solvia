using Solvia.Services;

namespace Solvia.Tests;

public sealed partial class ExtractionTests
{
    [Theory]
    [InlineData("I own a white Volvo 760 with license plate 234221 CA")]
    [InlineData("I own a white Volvo 760 and my license plate number is 234221 in California")]
    [InlineData("i own a white volvo 760 with license plate 234221 ca")]
    [InlineData("I OWN A WHITE VOLVO 760 WITH LICENSE PLATE 234221 CA")]
    [InlineData("I Own a White Volvo 760 and My License Plate Number Is 234221 in CALIFORNIA")]
    [InlineData("I own a WHITE Volvo 760; LICENSE PLATE NUMBER: 234221, California.")]
    public void OwnershipSentence_ExtractsTheActualNumericPlateAndVehicleWithoutInventingPermitChoices(string sentence)
    {
        var result = new FallbackExtractor(Catalog).Extract(sentence);

        Assert.Equal("234221", result.Plate);
        Assert.Equal("Volvo", result.CarMake);
        Assert.Equal("760", result.CarModel);
        Assert.Equal("White", result.CarColor);
        Assert.Equal("CA", result.CarState);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Null(result.College);
        Assert.Null(result.PermitType);
        Assert.Null(result.Term);
    }

    [Theory]
    [InlineData("I own a white Volvo 760 in California")]
    [InlineData("I own a white Volvo 760 and my license plate number is unknown in California")]
    [InlineData("I own a white Volvo 760 and my license plate might be 234221 or 234222 in California")]
    public void OwnershipSentence_AbsentOrUncertainPlateDoesNotTurnTheModelNumberIntoAPlate(string sentence)
    {
        var result = new FallbackExtractor(Catalog).Extract(sentence);

        Assert.Null(result.Plate);
        Assert.Equal("Volvo", result.CarMake);
        Assert.Equal("760", result.CarModel);
        Assert.Equal("White", result.CarColor);
        Assert.Equal("CA", result.CarState);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Theory]
    [InlineData("I own a white Volvo 760, license plate 234221 NV", "NV")]
    [InlineData("I own a white Volvo 760, license plate 234221 in ca", "CA")]
    [InlineData("I own a white Volvo 760, license plate ABC-123, WA", "WA")]
    [InlineData("I own a white Volvo 760, license plate 234221 IN", "IN")]
    [InlineData("I own a white Volvo 760, license plate 234221 in town", null)]
    [InlineData("I own a white Volvo 760, license plate 8XYZ123 in town", null)]
    [InlineData("I own a white Volvo 760, license plate 234221; contact me", null)]
    [InlineData("I own a white Volvo 760 CA", null)]
    public void OwnershipSentence_StateCodeIsGroundedBesideAPlateRatherThanModelOrProse(string sentence, string? expected)
    {
        var result = new FallbackExtractor(Catalog).Extract(sentence);

        Assert.Equal(expected, result.CarState);
        Assert.Equal("Volvo", result.CarMake);
        Assert.Equal("760", result.CarModel);
        Assert.Equal("White", result.CarColor);
    }

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
