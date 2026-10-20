using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

public sealed partial class ExtractionTests
{
    [Theory]
    [InlineData("fallacy", "Term")]
    [InlineData("waterfall", "Term")]
    [InlineData("springboard", "Term")]
    [InlineData("summertime", "Term")]
    [InlineData("autumnal", "Term")]
    [InlineData("Fall", "weekday")]
    [InlineData("Fall", "someday")]
    [InlineData("Fall", "longterm")]
    [InlineData("Fall", "not Daily")]
    public async Task ModelVocabulary_RejectsSubstringsForBothProviders(string term, string permitType)
    {
        foreach (var result in await ExtractVocabularyThroughBothProviders(term, permitType))
        {
            Assert.Equal(ExtractionSource.Fallback, result.Source);
        }
    }

    [Theory]
    [InlineData(" fAlL ", "DaY PaSs", TermKind.Fall, PermitTypeCode.Daily)]
    [InlineData("aUtUmN", "SeMeStEr", TermKind.Fall, PermitTypeCode.Term)]
    [InlineData("sPrInG", "term permit", TermKind.Spring, PermitTypeCode.Term)]
    [InlineData("SUMMER", "DAILY", TermKind.Summer, PermitTypeCode.Daily)]
    [InlineData("Fall", "day", TermKind.Fall, PermitTypeCode.Daily)]
    [InlineData("Fall", "term / semester permit", TermKind.Fall, PermitTypeCode.Term)]
    public async Task ModelVocabulary_AcceptsExactNamesAndAliasesForBothProviders(
        string term, string permitType, TermKind expectedTerm, PermitTypeCode expectedType)
    {
        var results = await ExtractVocabularyThroughBothProviders(term, permitType);
        Assert.Equal(ExtractionSource.Ollama, results[0].Source);
        Assert.Equal(ExtractionSource.Meta, results[1].Source);
        Assert.All(results, result =>
        {
            Assert.Equal(expectedTerm, result.Term);
            Assert.Equal(expectedType, result.PermitType);
        });
    }

    private static async Task<ExtractionResult[]> ExtractVocabularyThroughBothProviders(string term, string permitType)
    {
        // Complete eight-field JSON tests the semantic vocabulary rather than
        // triggering Meta's missing-field rejection. Both transports are fake.
        var modelJson = JsonSerializer.Serialize(new
        {
            college = "Fresno City College", permitType, term, carMake = "Honda",
            carModel = "Civic", carColor = "Blue", plate = "8XYZ123", carState = "CA"
        });
        var ollama = NewService(new FakeOllamaHandler { ResponseBody = ChatEnvelope(modelJson) });
        var metaHandler = new FakeOllamaHandler
        {
            ResponseBody = JsonSerializer.Serialize(new
            {
                choices = new[] { new { finish_reason = "stop", message = new { content = modelJson, refusal = (string?)null } } }
            })
        };
        var meta = new MetaExtractionService(new HttpClient(metaHandler),
            Options.Create(new MetaOptions { ApiKey = "offline-vocabulary-test-placeholder" }),
            Catalog, new FallbackExtractor(Catalog), NullLogger<MetaExtractionService>.Instance);
        return [await ollama.ExtractAsync(PinnedSentence), await meta.ExtractAsync(PinnedSentence)];
    }
}
