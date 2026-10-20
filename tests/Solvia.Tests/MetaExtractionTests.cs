using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline adapter/contract tests. Every HTTP response uses an in-memory handler.</summary>
public sealed class MetaExtractionTests
{
    private const string FictionalText = "My car is a green Rivian R1S, license plate RV234221, CA.";
    private const string ModelJson = """
        {"college":null,"permitType":null,"term":null,"carMake":"Rivian",
         "carModel":"R1S","carColor":"Green","plate":"RV234221","carState":"CA"}
        """;
    private const string TestKey = "unit-test-placeholder-not-a-real-key";
    private static readonly FeeCatalogService Catalog = new(RedoTestDb.FindFeesCsv());

    [Fact]
    public async Task StandardMetaResponse_UsesSharedValidationAndHonestSource()
    {
        var handler = new MetaHandler { ResponseBody = Envelope(ModelJson) };
        var result = await Service(handler).ExtractAsync(FictionalText);

        Assert.Equal(ExtractionSource.Meta, result.Source);
        Assert.Equal("Rivian", result.CarMake);
        Assert.Equal("R1S", result.CarModel);
        Assert.Equal("Green", result.CarColor);
        Assert.Equal("RV234221", result.Plate);
        Assert.Equal("CA", result.CarState);
        Assert.Equal("meta", result.Source.ToWireName());
    }

    [Fact]
    public async Task Request_UsesOfficialEndpointServerAuthStrictSchemaAndBoundedGeneration()
    {
        var handler = new MetaHandler { ResponseBody = Envelope(ModelJson) };
        await Service(handler).ExtractAsync(FictionalText);

        Assert.Equal("https://api.meta.ai/v1/chat/completions", handler.RequestUri);
        Assert.Equal("Bearer", handler.AuthScheme);
        Assert.Equal(TestKey, handler.AuthValue);
        Assert.DoesNotContain(TestKey, handler.RequestBody);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        Assert.Equal("muse-spark-1.3", root.GetProperty("model").GetString());
        Assert.Equal("minimal", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal(1024, root.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.GetProperty("store").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schemaSettings = format.GetProperty("json_schema");
        Assert.True(schemaSettings.GetProperty("strict").GetBoolean());
        var schema = schemaSettings.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(8, schema.GetProperty("required").GetArrayLength());
        Assert.False(schema.GetProperty("properties").TryGetProperty("price", out _));
        Assert.False(schema.GetProperty("properties").TryGetProperty("paymentStatus", out _));
        Assert.Contains("Fresno City College", schema.GetProperty("properties").GetProperty("college").ToString());
        Assert.Equal(FictionalText, root.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("", "muse-spark-1.3")]
    [InlineData(TestKey, "muse-spark-1.3-contributor")]
    [InlineData(TestKey, "unapproved-model")]
    public async Task MissingKeyOrNonStandardModel_DoesNotSendARequest(string key, string model)
    {
        var handler = new MetaHandler { ResponseBody = Envelope(ModelJson) };
        var result = await Service(handler, key: key, model: model).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(302)]
    public async Task ProviderErrors_FallBackWithoutLoggingSensitiveErrorBody(int status)
    {
        var logs = new SafeTestLogger();
        var handler = new MetaHandler { Status = (HttpStatusCode)status, ResponseBody = TestKey + FictionalText };
        var result = await Service(handler, logger: logs).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal(1, handler.Calls);
        Assert.All(logs.Messages, message =>
        {
            Assert.DoesNotContain(TestKey, message);
            Assert.DoesNotContain(FictionalText, message);
        });
    }

    [Theory]
    [InlineData("not JSON")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"{}\"}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"{}\",\"refusal\":\"Cannot answer\"}}]}")]
    public async Task UnusableCompletion_FallsBack(string response)
    {
        var result = await Service(new MetaHandler { ResponseBody = response }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"plate\":\"RV234221\"}")]
    [InlineData("{\"college\":null,\"permitType\":null,\"term\":null,\"carMake\":true,\"carModel\":null,\"carColor\":null,\"plate\":\"RV234221\",\"carState\":null}")]
    public async Task ViolatedStructuredShape_IsNotLabeledAsMetaSuccess(string modelJson)
    {
        var result = await Service(new MetaHandler { ResponseBody = Envelope(modelJson) }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Fact]
    public async Task UnknownCampus_IsRejectedByTheSharedContract()
    {
        var invalid = ModelJson.Replace("\"college\":null", "\"college\":\"Invented University\"");
        var result = await Service(new MetaHandler { ResponseBody = Envelope(invalid) }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Theory]
    [InlineData("white Volvo 760, license plate 234221", "234221")]
    [InlineData("white Volvo 760 without a license plate", null)]
    [InlineData("white Volvo 760; plate might be 234221 or 234222", null)]
    public async Task ModelPlate_RemainsGroundedInThePersonsWords(string words, string? expected)
    {
        var result = await Service(new MetaHandler { ResponseBody = Envelope(ModelJson) }).ExtractAsync(words);
        Assert.Equal(ExtractionSource.Meta, result.Source);
        Assert.Equal(expected, result.Plate);
    }

    [Fact]
    public async Task ConfiguredTimeout_FallsBackAndDoesNotWaitForTheProvider()
    {
        var handler = new MetaHandler { ResponseBody = Envelope(ModelJson), Delay = TimeSpan.FromSeconds(2) };
        var result = await Service(handler, timeoutSeconds: 1).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Fact]
    public async Task TransportFailure_DoesNotLogExceptionMessageOrCredentials()
    {
        var logs = new SafeTestLogger();
        var handler = new MetaHandler { Failure = new HttpRequestException(TestKey + FictionalText) };
        var result = await Service(handler, logger: logs).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Contains(logs.Messages, message => message.Contains(nameof(HttpRequestException)));
        Assert.All(logs.Messages, message =>
        {
            Assert.DoesNotContain(TestKey, message);
            Assert.DoesNotContain(FictionalText, message);
        });
    }

    [Fact]
    public async Task CallerCancellation_IsNotSwallowedOrSentToProvider()
    {
        var handler = new MetaHandler { ResponseBody = Envelope(ModelJson) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(handler).ExtractAsync(FictionalText, cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private static MetaExtractionService Service(MetaHandler handler, string key = TestKey,
        string model = "muse-spark-1.3", int timeoutSeconds = 10, SafeTestLogger? logger = null) =>
        new(new HttpClient(handler), Options.Create(new MetaOptions { ApiKey = key, Model = model, TimeoutSeconds = timeoutSeconds }),
            Catalog, new FallbackExtractor(Catalog), logger ?? new SafeTestLogger());

    private static string Envelope(string modelJson) => JsonSerializer.Serialize(new
    {
        model = "muse-spark-1.3",
        choices = new[] { new { finish_reason = "stop", message = new { content = modelJson, refusal = (string?)null } } }
    });

    private sealed class MetaHandler : HttpMessageHandler
    {
        public string ResponseBody { get; init; } = "";
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public TimeSpan Delay { get; init; }
        public Exception? Failure { get; init; }
        public int Calls { get; private set; }
        public string? RequestUri { get; private set; }
        public string? AuthScheme { get; private set; }
        public string? AuthValue { get; private set; }
        public string RequestBody { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri!.AbsoluteUri;
            AuthScheme = request.Headers.Authorization?.Scheme;
            AuthValue = request.Headers.Authorization?.Parameter;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Failure is not null) throw Failure;
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class SafeTestLogger : ILogger<MetaExtractionService>
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
