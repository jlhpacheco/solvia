using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class ModelDeadlineTests
{
    [Theory]
    [InlineData("ollama", false)]
    [InlineData("ollama", true)]
    [InlineData("together", false)]
    [InlineData("together", true)]
    public async Task OutputPastMeasuredDeadline_IsRejectedEvenBeforeCancellationTimerDispatches(
        string provider, bool delayBody)
    {
        var catalog = new FeeCatalogService(RedoTestDb.FindFeesCsv());
        var fallback = new FallbackExtractor(catalog);
        var clock = new ManualDeadlineClock();
        var modelJson = """
            {"college":null,"permitType":null,"term":null,"carMake":"Volvo",
             "carModel":"760","carColor":"White","plate":"654321","carState":"CA"}
            """;
        var response = provider == "ollama"
            ? JsonSerializer.Serialize(new { message = new { content = modelJson } })
            : JsonSerializer.Serialize(new
            {
                model = TogetherOptions.SupportedModel,
                choices = new[] { new { finish_reason = "stop", message = new { content = modelJson } } }
            });
        var handler = new ClockAdvancingHandler(clock, response, delayBody);
        using var http = new HttpClient(handler);
        // Advance the monotonic clock, not the real timer. This deterministically
        // models delayed timer dispatch without sleeping or stressing the host.
        IAiExtractor extractor = provider == "ollama"
            ? new OllamaExtractionService(http,
                Options.Create(new OllamaOptions { TimeoutSeconds = 90 }), catalog, fallback,
                NullLogger<OllamaExtractionService>.Instance, timeProvider: clock)
            : new TogetherExtractionService(http,
                Options.Create(new TogetherOptions
                {
                    ApiKey = "offline-placeholder-not-a-credential", TimeoutSeconds = 90
                }), catalog, fallback, NullLogger<TogetherExtractionService>.Instance, timeProvider: clock);

        var result = await extractor.ExtractAsync("Fictional white Volvo 760, license plate 654321");

        Assert.Equal(1, handler.Calls);
        Assert.False(handler.TokenWasCanceledWhenResponseReturned);
        Assert.Equal(ExtractionSource.Fallback, result.Source);
        Assert.Equal("654321", result.Plate);
    }

    private sealed class ManualDeadlineClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void AdvancePastBudget() => _timestamp += TimeSpan.FromSeconds(91).Ticks;
    }

    private sealed class ClockAdvancingHandler(ManualDeadlineClock clock, string body, bool delayBody)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public bool TokenWasCanceledWhenResponseReturned { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (!delayBody) clock.AdvancePastBudget();
            TokenWasCanceledWhenResponseReturned = cancellationToken.IsCancellationRequested;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = delayBody
                    ? new ClockAdvancingContent(body, clock.AdvancePastBudget)
                    : new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class ClockAdvancingContent(string body, Action advanceClock) : HttpContent
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(body);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            advanceClock();
            await stream.WriteAsync(_bytes);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _bytes.Length;
            return true;
        }
    }
}
