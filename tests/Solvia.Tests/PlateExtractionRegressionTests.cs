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
    public void CompoundPlateLabels_KeepOnlyThePlate(string sentence, string expected)
    {
        Assert.Equal(expected, new FallbackExtractor(Catalog).Extract(sentence).Plate);
    }

    [Theory]
    [InlineData("My license plate is unknown")]
    [InlineData("I don't have a license plate yet")]
    [InlineData("license plate number")]
    [InlineData("plate ABC123 and plate XYZ987")]
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
}
