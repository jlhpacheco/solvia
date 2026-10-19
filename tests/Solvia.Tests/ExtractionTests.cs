using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Models.Enums;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// T3 AI-extraction tests — fully OFFLINE. A fake
/// <see cref="HttpMessageHandler"/> plays Ollama (valid JSON, garbage,
/// unknown campus, HTTP error, hang), and a throwing handler plays
/// "Ollama is not running". No real model call ever leaves the test run;
/// the live Phi-4-mini run is the plan's done-test on Jose Luis's PC.
///
/// What is proven here:
/// - valid model JSON → Source=Ollama with the validated fields, the
///   college resolved against the REAL fee catalog;
/// - the request asks phi4-mini for ONLY JSON in our schema, with the
///   10 catalog campus names embedded and prices explicitly excluded;
/// - garbage JSON / connection refused / HTTP 500 / timeout → the
///   deterministic fallback answers, Source=Fallback, nothing throws;
/// - a model answer naming a college the catalog does not know is
///   rejected wholesale — never guessed, never passed through;
/// - a messy college name from the model ("fresno city") is re-matched
///   deterministically to the catalog's canonical campus;
/// - the deterministic fallback extracts the plan's Maria sentence
///   exactly, resolves a day pass, and returns null — never a guess —
///   for unknown and ambiguous colleges;
/// - the draft type has NO price member: the AI can never set a price.
/// </summary>
public sealed class ExtractionTests
{
    private const string MariaSentence =
        "I study at Fresno City College, my car is a blue 2019 Honda Civic, " +
        "plate 8XYZ123, I need the fall term";

    private const string ValidModelJson = """
        {
          "college": "Fresno City College",
          "permitType": "Term",
          "term": "Fall",
          "carMake": "Honda",
          "carModel": "Civic",
          "carColor": "Blue",
          "plate": "8XYZ123",
          "carState": "CA"
        }
        """;

    private static readonly FeeCatalogService Catalog = new(FindFeesCsv());

    private static string FindFeesCsv()
    {
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(ExtractionTests).Assembly.Location),
            AppContext.BaseDirectory,
        };

        foreach (var start in starts)
        {
            var dir = start is null ? null : new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "data", "fees.csv");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        throw new FileNotFoundException(
            "Could not locate data/fees.csv above the test assembly.");
    }

    // ------------------------------------------------------------------
    // The AI path, when Ollama answers well
    // ------------------------------------------------------------------

    [Fact]
    public async Task ValidModelJson_ReturnsOllamaSourceWithValidatedFields()
    {
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope(ValidModelJson) };
        var service = NewService(handler);

        var result = await service.ExtractAsync(MariaSentence);

        Assert.Equal(ExtractionSource.Ollama, result.Source);
        Assert.Equal("Fresno City College", result.College);
        Assert.Equal(PermitTypeCode.Term, result.PermitType);
        Assert.Equal(TermKind.Fall, result.Term);
        Assert.Equal("Honda", result.CarMake);
        Assert.Equal("Civic", result.CarModel);
        Assert.Equal("Blue", result.CarColor);
        Assert.Equal("8XYZ123", result.Plate);
        Assert.Equal("CA", result.CarState);
    }

    [Fact]
    public async Task ModelRequest_AsksPhi4MiniForOnlyJsonWithTheCatalogColleges()
    {
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope(ValidModelJson) };
        var service = NewService(handler);

        await service.ExtractAsync(MariaSentence);

        Assert.Equal("/api/chat", Assert.Single(handler.RequestPaths));
        using var body = JsonDocument.Parse(Assert.Single(handler.RequestBodies));
        var root = body.RootElement;
        Assert.Equal("phi4-mini", root.GetProperty("model").GetString());
        Assert.Equal("json", root.GetProperty("format").GetString());
        Assert.False(root.GetProperty("stream").GetBoolean());

        var messages = root.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        var system = messages[0].GetProperty("content").GetString()!;
        Assert.Contains("ONLY a JSON object", system);
        Assert.Contains("Fresno City College", system);   // catalog list embedded
        Assert.Contains("CSU Monterey Bay", system);
        Assert.Contains("Never output a price", system);  // prices excluded
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal(MariaSentence, messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task MessyCollegeNameFromModel_IsResolvedAgainstTheCatalog()
    {
        var content = """
            { "college": "fresno city", "permitType": "Term", "term": "Fall",
              "carMake": "Honda", "carModel": "Civic", "carColor": "Blue",
              "plate": "8XYZ123", "carState": "CA" }
            """;
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope(content) };
        var service = NewService(handler);

        var result = await service.ExtractAsync(MariaSentence);

        Assert.Equal(ExtractionSource.Ollama, result.Source);
        Assert.Equal("Fresno City College", result.College); // canonical name
    }

    [Fact]
    public async Task EmptyModelJson_ReturnsOllamaSourceWithMissingFieldsNull()
    {
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope("{}") };
        var service = NewService(handler);

        var result = await service.ExtractAsync("I need a permit");

        // A schema-valid but empty answer stands as the AI's answer;
        // missing fields stay null and the flow asks for them.
        Assert.Equal(ExtractionSource.Ollama, result.Source);
        Assert.Null(result.College);
        Assert.Null(result.Term);
        Assert.Null(result.PermitType);
        Assert.Null(result.Plate);
    }

    // ------------------------------------------------------------------
    // Every failure lands in the fallback — the demo never dies
    // ------------------------------------------------------------------

    [Fact]
    public async Task GarbageModelOutput_FallsBack()
    {
        var handler = new FakeOllamaHandler
        {
            ResponseBody = ChatEnvelope(
                "Sure! Maria studies at Fresno City College and needs a fall term permit."),
        };
        var service = NewService(handler);

        var result = await service.ExtractAsync(MariaSentence);

        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("Fresno City College", result.College);
        Assert.Equal(TermKind.Fall, result.Term);
    }

    [Fact]
    public async Task OllamaConnectionRefused_FallsBackWithoutThrowing()
    {
        var handler = new FakeOllamaHandler
        {
            ExceptionToThrow = new HttpRequestException(
                "Connection refused (localhost:11434)"),
        };
        var service = NewService(handler);

        var result = await service.ExtractAsync(MariaSentence); // must not throw

        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("Fresno City College", result.College);
        Assert.Equal(TermKind.Fall, result.Term);
        Assert.Equal("8XYZ123", result.Plate);
    }

    [Fact]
    public async Task OllamaHttpError_FallsBack()
    {
        var handler = new FakeOllamaHandler
        {
            StatusCode = HttpStatusCode.InternalServerError,
            ResponseBody = """{ "error": "model exploded" }""",
        };
        var service = NewService(handler);

        var result = await service.ExtractAsync(MariaSentence);

        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("Fresno City College", result.College);
    }

    [Fact]
    public async Task OllamaTimeout_FallsBack()
    {
        var handler = new FakeOllamaHandler { Delay = TimeSpan.FromSeconds(30) };
        var service = NewService(handler, timeout: TimeSpan.FromMilliseconds(300));

        var result = await service.ExtractAsync(MariaSentence);

        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("Fresno City College", result.College);
    }

    [Fact]
    public async Task UnknownCampusInModelOutput_IsRejectedToFallback()
    {
        var content = """
            { "college": "Harvard University", "permitType": "Term", "term": "Fall",
              "carMake": "Honda", "carModel": "Civic", "carColor": "Blue",
              "plate": "8XYZ123", "carState": "CA" }
            """;
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope(content) };
        var service = NewService(handler);

        var result = await service.ExtractAsync(MariaSentence);

        // The whole AI answer is rejected — Harvard is NOT passed
        // through, and the fallback's read of the student's own words
        // (Fresno) is what stands.
        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("Fresno City College", result.College);
    }

    // ------------------------------------------------------------------
    // The AI never sets the price
    // ------------------------------------------------------------------

    [Fact]
    public async Task ModelJsonWithPriceFields_ExtractsCleanlyAndIgnoresThem()
    {
        var content = """
            { "college": "Fresno City College", "permitType": "Term", "term": "Fall",
              "carMake": "Honda", "carModel": "Civic", "carColor": "Blue",
              "plate": "8XYZ123", "carState": "CA",
              "price": 999.99, "priceCents": 1, "amount": "0.01" }
            """;
        var handler = new FakeOllamaHandler { ResponseBody = ChatEnvelope(content) };
        var service = NewService(handler);

        var result = await service.ExtractAsync(MariaSentence);

        Assert.Equal(ExtractionSource.Ollama, result.Source);
        Assert.Equal("Fresno City College", result.College);
        // There is nowhere on the result for the model's invented
        // "price" to land — see the reflection test below.
    }

    [Fact]
    public void ExtractionResult_HasNoPriceMemberByDesign()
    {
        // The strongest form of "the AI never sets the price": the
        // draft type has no price-shaped member at all, so no code
        // path — model, fallback, or future — can put a price on it.
        // The price comes only from IFeeCatalog, later.
        var offenders = typeof(ExtractionResult)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name =>
                name.Contains("price", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("cents", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("amount", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("cost", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("fee", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }

    // ------------------------------------------------------------------
    // The deterministic fallback, on its own
    // ------------------------------------------------------------------

    [Fact]
    public void Fallback_ExtractsTheMariaSentence()
    {
        var fallback = new FallbackExtractor(Catalog);

        var result = fallback.Extract(MariaSentence);

        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("Fresno City College", result.College);
        Assert.Equal(PermitTypeCode.Term, result.PermitType);
        Assert.Equal(TermKind.Fall, result.Term);
        Assert.Equal("Honda", result.CarMake);
        Assert.Equal("Civic", result.CarModel);
        Assert.Equal("Blue", result.CarColor);
        Assert.Equal("8XYZ123", result.Plate);
        Assert.Null(result.CarState); // not stated — SCHEMA defaults CA later
    }

    [Fact]
    public void Fallback_DayPassForFresnoCityCollege()
    {
        var fallback = new FallbackExtractor(Catalog);

        var result = fallback.Extract("I need a day pass for Fresno City College tomorrow");

        Assert.Equal("Fresno City College", result.College);
        Assert.Equal(PermitTypeCode.Daily, result.PermitType);
        Assert.Null(result.Term);
    }

    [Theory]
    [InlineData("I need the spring semester at Diablo Valley College",
        "Diablo Valley College", TermKind.Spring)]
    [InlineData("summer term at Antelope Valley College for my Toyota Camry",
        "Antelope Valley College", TermKind.Summer)]
    public void Fallback_TermsAndCampuses(
        string sentence, string expectedCollege, TermKind expectedTerm)
    {
        var fallback = new FallbackExtractor(Catalog);

        var result = fallback.Extract(sentence);

        Assert.Equal(expectedCollege, result.College);
        Assert.Equal(expectedTerm, result.Term);
        Assert.Equal(PermitTypeCode.Term, result.PermitType);
    }

    [Fact]
    public void Fallback_UnknownCollege_ReturnsNullCollege_NeverAGuess()
    {
        var fallback = new FallbackExtractor(Catalog);

        var result = fallback.Extract(
            "I study at Hogwarts and drive a Ford, I need the spring term");

        Assert.Null(result.College);
        Assert.Equal(TermKind.Spring, result.Term); // the rest still extracts
        Assert.Equal("Ford", result.CarMake);
    }

    [Fact]
    public void Fallback_AmbiguousValley_ReturnsNullCollege()
    {
        // "Valley" alone is shared by TWO catalog campuses (Antelope
        // Valley College and Diablo Valley College) — no guess.
        var fallback = new FallbackExtractor(Catalog);

        var result = fallback.Extract("I go to Valley, I need the fall term");

        Assert.Null(result.College);
        Assert.Equal(TermKind.Fall, result.Term);
    }

    [Fact]
    public void Fallback_Bakersfield_ResolvesToCsuBakersfield()
    {
        // Bakersfield College published no semester price and was
        // removed from the table (Oct 18 order), so "Bakersfield"
        // now names exactly one catalog campus.
        var fallback = new FallbackExtractor(Catalog);

        var result = fallback.Extract("I go to Bakersfield, I need the fall term");

        Assert.Equal("CSU Bakersfield", result.College);
        Assert.Equal(TermKind.Fall, result.Term);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static OllamaExtractionService NewService(
        FakeOllamaHandler handler, TimeSpan? timeout = null)
    {
        var http = new HttpClient(handler);
        var options = Options.Create(new OllamaOptions
        {
            BaseUrl = "http://localhost:11434",
            Model = "phi4-mini",
        });
        return new OllamaExtractionService(
            http,
            options,
            Catalog,
            new FallbackExtractor(Catalog),
            NullLogger<OllamaExtractionService>.Instance,
            timeout);
    }

    /// <summary>Wraps model content in Ollama's /api/chat response envelope.</summary>
    private static string ChatEnvelope(string content) =>
        JsonSerializer.Serialize(new
        {
            model = "phi4-mini",
            message = new { role = "assistant", content },
            done = true,
        });

    /// <summary>
    /// An offline stand-in for Ollama: records every request and then
    /// answers with the configured body/status — or throws / hangs, to
    /// play a model that is down or stuck.
    /// </summary>
    private sealed class FakeOllamaHandler : HttpMessageHandler
    {
        public List<string> RequestPaths { get; } = [];
        public List<string> RequestBodies { get; } = [];

        public string ResponseBody { get; set; } = "";
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public Exception? ExceptionToThrow { get; set; }
        public TimeSpan Delay { get; set; } = TimeSpan.Zero;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri!.AbsolutePath);
            RequestBodies.Add(request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken));

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
