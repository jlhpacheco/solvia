using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

public sealed partial class ExtractionTests
{
    [Fact]
    public async Task ConfiguredOneSecondBudget_CancelsTwoSecondModelResponse()
    {
        // This valid response would be accepted under the default 10-second
        // budget. Fallback proves that the configured shorter budget is used.
        // The fake handler is in-memory; this test never contacts a model.
        var handler = new FakeOllamaHandler
        {
            Delay = TimeSpan.FromSeconds(2),
            ResponseBody = ChatEnvelope(ValidModelJson)
        };
        var result = await NewService(handler, timeoutSeconds: 1).ExtractAsync(PinnedSentence);

        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("8XYZ123", result.Plate);
        Assert.Single(handler.RequestPaths);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(90, 90)]
    [InlineData(91, 90)]
    [InlineData(int.MaxValue, 90)]
    public void ModelBudgetConfiguration_IsBounded(int configured, int expected)
    {
        Assert.Equal(expected, new OllamaOptions { TimeoutSeconds = configured }.TimeoutSeconds);
    }

    [Fact]
    public void DefaultModelBudget_RemainsTenSeconds()
    {
        Assert.Equal(10, new OllamaOptions().TimeoutSeconds);
    }
}
