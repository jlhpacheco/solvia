using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Offline adapter/contract tests. Every HTTP response uses an in-memory handler.</summary>
public sealed class TogetherExtractionTests
{
    private const string FictionalText = "My car is a green Rivian R1S, license plate RV234221, CA.";
    private const string ModelJson = """
        {"college":null,"permitType":null,"term":null,"carMake":"Rivian",
         "carModel":"R1S","carColor":"Green","plate":"RV234221","carState":"CA"}
        """;
    private const string TestKey = "unit-test-placeholder-not-a-real-key";
    private static readonly FeeCatalogService Catalog = new(RedoTestDb.FindFeesCsv());

    [Fact]
    public async Task TogetherResponse_UsesSharedValidationAndHonestSource()
    {
        var handler = new TogetherHandler { ResponseBody = Envelope(ModelJson) };
        var result = await Service(handler).ExtractAsync(FictionalText);

        Assert.Equal(ExtractionSource.Together, result.Source);
        Assert.Equal("Rivian", result.CarMake);
        Assert.Equal("R1S", result.CarModel);
        Assert.Equal("Green", result.CarColor);
        Assert.Equal("RV234221", result.Plate);
        Assert.Equal("CA", result.CarState);
        Assert.Equal("together", result.Source.ToWireName());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("openai/gpt-oss-20b")]
    [InlineData("Qwen/Qwen3.5-9B")]
    [InlineData("openai/gpt-oss-120b-other")]
    public async Task MissingOrUnexpectedReturnedModel_IsNeverLabeledAsTheSelectedModel(string? returnedModel)
    {
        var response = JsonSerializer.Serialize(new
        {
            model = returnedModel,
            choices = new[] { new { finish_reason = "stop", message = new { content = ModelJson } } }
        });
        var result = await Service(new TogetherHandler { ResponseBody = response }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Fact]
    public async Task ReasoningMetadata_IsIgnored_AndCannotReplaceFinalContent()
    {
        const string reasoning = "private-reasoning-placeholder";
        var logs = new SafeTestLogger();
        string Response(string? content) => JsonSerializer.Serialize(new
        {
            model = TogetherOptions.SupportedModel,
            choices = new[] { new { finish_reason = "stop", message = new
            {
                content, reasoning = reasoning + ModelJson, tool_calls = Array.Empty<object>()
            } } }
        });
        var valid = await Service(new TogetherHandler { ResponseBody = Response(ModelJson) }, logger: logs).ExtractAsync(FictionalText);
        var reasoningOnly = await Service(new TogetherHandler { ResponseBody = Response(null) }, logger: logs).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Together, valid.Source);
        Assert.Equal(ExtractionSource.Fallback, reasoningOnly.Source);
        Assert.DoesNotContain(reasoning, string.Join(" ", logs.Messages));
    }

    [Theory]
    [InlineData("tools")]
    [InlineData("scalar")]
    [InlineData("function")]
    public async Task ToolOrFunctionCall_IsRejectedDespiteOtherwiseValidJson(string special)
    {
        var message = new Dictionary<string, object?> { ["content"] = ModelJson };
        if (special == "tools") message["tool_calls"] = new[] { new { name = "buy_permit" } };
        if (special == "scalar") message["tool_calls"] = "invalid";
        if (special == "function") message["function_call"] = new { name = "set_price" };
        var response = JsonSerializer.Serialize(new
        {
            model = TogetherOptions.SupportedModel,
            choices = new[] { new { finish_reason = "stop", message } }
        });
        var result = await Service(new TogetherHandler { ResponseBody = response }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizeProviderBody_IsDiscardedBeforeTrustingContent(bool unknownLength)
    {
        var body = new string(' ', 65 * 1024) + Envelope(ModelJson);
        var handler = new TogetherHandler
        {
            ResponseBody = body,
            Content = unknownLength ? new UnknownLengthContent(body) : null
        };
        var result = await Service(handler).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Fact]
    public async Task Request_UsesOfficialEndpointServerAuthStrictSchemaAndBoundedGeneration()
    {
        var handler = new TogetherHandler { ResponseBody = Envelope(ModelJson) };
        await Service(handler).ExtractAsync(FictionalText);

        Assert.Equal("https://api.together.ai/v1/chat/completions", handler.RequestUri);
        Assert.Equal("Bearer", handler.AuthScheme);
        Assert.Equal(TestKey, handler.AuthValue);
        Assert.DoesNotContain(TestKey, handler.RequestBody);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var root = request.RootElement;
        Assert.Equal("openai/gpt-oss-120b", root.GetProperty("model").GetString());
        Assert.False(root.TryGetProperty("reasoning", out _));
        Assert.Equal("low", root.GetProperty("reasoning_effort").GetString());
        Assert.Equal(1, root.GetProperty("temperature").GetInt32());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.False(root.TryGetProperty("tools", out _));
        var format = root.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        var schemaSettings = format.GetProperty("json_schema");
        var schema = schemaSettings.GetProperty("schema");
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(8, schema.GetProperty("required").GetArrayLength());
        Assert.False(schema.GetProperty("properties").TryGetProperty("price", out _));
        Assert.False(schema.GetProperty("properties").TryGetProperty("paymentStatus", out _));
        Assert.Contains("Fresno City College", schema.GetProperty("properties").GetProperty("college").ToString());
        Assert.Contains(schema.GetRawText(), root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("developer", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal(FictionalText, root.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Theory]
    [InlineData("", "openai/gpt-oss-120b")]
    [InlineData(TestKey, "moonshotai/Kimi-K3")]
    [InlineData(TestKey, "unapproved-model")]
    [InlineData(TestKey, "Qwen/Qwen3.5-9B")]
    [InlineData(TestKey, "openai/gpt-oss-20b")]
    public async Task MissingKeyOrUnapprovedModel_DoesNotSendARequest(string key, string model)
    {
        var handler = new TogetherHandler { ResponseBody = Envelope(ModelJson) };
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
        var handler = new TogetherHandler { Status = (HttpStatusCode)status, ResponseBody = TestKey + FictionalText };
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
        var result = await Service(new TogetherHandler { ResponseBody = response }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"plate\":\"RV234221\"}")]
    [InlineData("{\"college\":null,\"permitType\":null,\"term\":null,\"carMake\":true,\"carModel\":null,\"carColor\":null,\"plate\":\"RV234221\",\"carState\":null}")]
    public async Task ViolatedStructuredShape_IsNotLabeledAsTogetherSuccess(string modelJson)
    {
        var result = await Service(new TogetherHandler { ResponseBody = Envelope(modelJson) }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Fact]
    public async Task UnknownCampus_IsRejectedByTheSharedContract()
    {
        var invalid = ModelJson.Replace("\"college\":null", "\"college\":\"Invented University\"");
        var result = await Service(new TogetherHandler { ResponseBody = Envelope(invalid) }).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Theory]
    [InlineData("white Volvo 760, license plate 234221", "234221")]
    [InlineData("white Volvo 760 without a license plate", null)]
    [InlineData("white Volvo 760; plate might be 234221 or 234222", null)]
    public async Task ModelPlate_RemainsGroundedInThePersonsWords(string words, string? expected)
    {
        var result = await Service(new TogetherHandler { ResponseBody = Envelope(ModelJson) }).ExtractAsync(words);
        Assert.Equal(ExtractionSource.Together, result.Source);
        Assert.Equal(expected, result.Plate);
    }

    [Fact]
    public async Task ConfiguredTimeout_FallsBackAndDoesNotWaitForTheProvider()
    {
        var handler = new TogetherHandler { ResponseBody = Envelope(ModelJson), Delay = TimeSpan.FromSeconds(2) };
        var result = await Service(handler, timeoutSeconds: 1).ExtractAsync(FictionalText);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
    }

    [Fact]
    public async Task TransportFailure_DoesNotLogExceptionMessageOrCredentials()
    {
        var logs = new SafeTestLogger();
        var handler = new TogetherHandler { Failure = new HttpRequestException(TestKey + FictionalText) };
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
        var handler = new TogetherHandler { ResponseBody = Envelope(ModelJson) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(handler).ExtractAsync(FictionalText, cancellation.Token));
        Assert.Equal(0, handler.Calls);
    }

    private static TogetherExtractionService Service(TogetherHandler handler, string key = TestKey,
        string model = "openai/gpt-oss-120b", int timeoutSeconds = 10, SafeTestLogger? logger = null) =>
        new(new HttpClient(handler), Options.Create(new TogetherOptions { ApiKey = key, Model = model, TimeoutSeconds = timeoutSeconds }),
            Catalog, new FallbackExtractor(Catalog), logger ?? new SafeTestLogger());

    private static string Envelope(string modelJson) => JsonSerializer.Serialize(new
    {
        model = "openai/gpt-oss-120b",
        choices = new[] { new { finish_reason = "stop", message = new { content = modelJson, refusal = (string?)null } } }
    });

    private sealed class TogetherHandler : HttpMessageHandler
    {
        public string ResponseBody { get; init; } = "";
        public HttpStatusCode Status { get; init; } = HttpStatusCode.OK;
        public TimeSpan Delay { get; init; }
        public Exception? Failure { get; init; }
        public HttpContent? Content { get; init; }
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
            return new HttpResponseMessage(Status) { Content = Content ?? new StringContent(ResponseBody, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(Encoding.UTF8.GetBytes(body)).AsTask();
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class SafeTestLogger : ILogger<TogetherExtractionService>
    {
        public List<string> Messages { get; } = [];
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
