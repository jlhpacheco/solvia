using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class AiOperationStatusTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }
    private static AiOperationStatus Store(Clock clock, string provider = "together", string key = "OFFLINE-TEST-KEY") => new(
        Options.Create(new AiOptions { Provider = provider }), Options.Create(new TogetherOptions { ApiKey = key }),
        Options.Create(new OllamaOptions()), clock);
    private static void Result(AiOperationStatus store, string source, AiReaderKind kind = AiReaderKind.Vehicle)
    {
        using var observation = store.Observe(kind, out var complete);
        complete(source);
    }

    [Theory]
    [InlineData("together", "OFFLINE-TEST-KEY", "unchecked")]
    [InlineData("together", "", "unconfigured")]
    [InlineData("ollama", "", "unchecked")]
    public void CredentialsOrLocalConfigurationAlone_NeverClaimsRecentSuccess(string provider, string key, string expected)
    {
        var status = Store(new Clock(), provider, key).GetSnapshot();
        Assert.Equal(expected, status.State);
        Assert.Null(status.LastSuccessAtUtc);
        Assert.Null(status.LastAttemptAtUtc);
    }

    [Theory]
    [InlineData("together", "together")]
    [InlineData("ollama", "ollama")]
    public void ValidatedMatchingSource_RecordsRealObservedTime(string provider, string source)
    {
        var clock = new Clock(); var store = Store(clock, provider);
        Result(store, source);
        var status = store.GetSnapshot();
        Assert.Equal("recent", status.State);
        Assert.Equal(clock.GetUtcNow(), status.LastSuccessAtUtc);
        Assert.Equal(status.LastSuccessAtUtc, status.LastAttemptAtUtc);
    }

    [Theory]
    [InlineData("together", "ollama")]
    [InlineData("ollama", "together")]
    public void UnexpectedSourceCannotVerifySelectedModel(string provider, string source)
    {
        var store = Store(new Clock(), provider); Result(store, source);
        Assert.Equal("fallback", store.GetSnapshot().State);
        Assert.Null(store.GetSnapshot().LastSuccessAtUtc);
    }

    [Fact]
    public void LatestFallbackWinsWhileEarlierSuccessTimestampIsRetained()
    {
        var clock = new Clock(); var store = Store(clock);
        Result(store, "together"); var success = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromSeconds(10)); Result(store, "fallback", AiReaderKind.Schedule);
        Assert.Equal("fallback", store.GetSnapshot().State);
        Assert.Equal(success, store.GetSnapshot().LastSuccessAtUtc);
        Assert.Equal(clock.GetUtcNow(), store.GetSnapshot().LastAttemptAtUtc);
        Assert.Null(store.GetSnapshot().Readers[1].LastSuccessAtUtc);
    }

    [Fact]
    public void TenMinuteExpiryAndClockRollbackDoNotLeaveAGreenStatus()
    {
        var clock = new Clock(); var store = Store(clock); Result(store, "together");
        clock.Advance(TimeSpan.FromMinutes(10)); Assert.Equal("stale", store.GetSnapshot().State);
        clock.Advance(TimeSpan.FromMinutes(-11)); Assert.Equal("stale", store.GetSnapshot().State);
    }

    [Fact]
    public void ConcurrentReadersAreBoundedAndLatestCompletionControlsTheLabel()
    {
        var clock = new Clock(); var store = Store(clock);
        using var first = store.Observe(AiReaderKind.Vehicle, out var vehicle);
        using var second = store.Observe(AiReaderKind.Schedule, out var schedule);
        Assert.Equal("reading", store.GetSnapshot().State);
        schedule("together"); Assert.Equal("reading", store.GetSnapshot().State);
        clock.Advance(TimeSpan.FromSeconds(1)); vehicle("fallback");
        Assert.Equal("fallback", store.GetSnapshot().State);
        Assert.Equal(2, store.GetSnapshot().Readers.Count);
        // Duplicate completion/disposal must not change the observed outcome.
        vehicle("together"); first.Dispose(); second.Dispose();
        Assert.Equal("fallback", store.GetSnapshot().State);
    }

    [Fact]
    public void CallerCancellationIsInterrupted_NotAnOutageOrANewSuccess()
    {
        var clock = new Clock(); var store = Store(clock); Result(store, "together");
        var previous = store.GetSnapshot().LastSuccessAtUtc;
        clock.Advance(TimeSpan.FromSeconds(1));
        store.Observe(AiReaderKind.Vehicle, out _).Dispose();
        Assert.Equal("interrupted", store.GetSnapshot().State);
        Assert.Equal(previous, store.GetSnapshot().LastSuccessAtUtc);
    }

    [Fact]
    public void LocalModeDoesNotClaimThatTheVisitReaderIsHostedAi()
    {
        var store = Store(new Clock(), "ollama");
        Result(store, "together", AiReaderKind.Schedule);
        Assert.Equal("disabled", store.GetSnapshot().Readers[1].State);
        Assert.Null(store.GetSnapshot().LastSuccessAtUtc);
    }

    private sealed class Extractor(Task<ExtractionResult> result) : IAiExtractor
    {
        public Task<ExtractionResult> ExtractAsync(string text, CancellationToken cancellationToken = default) => result;
    }
    private sealed class ScheduleReader(Task<VisitScheduleDraft> result) : IVisitScheduleReader
    {
        public Task<VisitScheduleDraft> ReadAsync(string text, CancellationToken cancellationToken = default) => result;
    }

    [Fact]
    public async Task VehicleDecoratorPreservesResultAndTracksOnlyValidatedSource()
    {
        var store = Store(new Clock());
        var pending = new TaskCompletionSource<ExtractionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = new ObservedAiExtractor(new Extractor(pending.Task), store);
        var read = reader.ExtractAsync("PRIVATE-FICTIONAL-INPUT");
        Assert.Equal("reading", store.GetSnapshot().State);
        var result = new ExtractionResult { Source = ExtractionSource.Together, Plate = "PRIVATE-PLATE" };
        pending.SetResult(result); Assert.Same(result, await read);
        Assert.Equal("recent", store.GetSnapshot().State);
        var json = JsonSerializer.Serialize(store.GetSnapshot());
        Assert.DoesNotContain("PRIVATE", json);
        Assert.DoesNotContain("OFFLINE-TEST-KEY", json);
    }

    [Fact]
    public async Task VisitDecoratorPreservesFallbackAndPropagatesCancellation()
    {
        var store = Store(new Clock());
        var draft = new VisitScheduleDraft([], null, "fallback", "Review draft");
        Assert.Same(draft, await new ObservedVisitScheduleReader(new ScheduleReader(Task.FromResult(draft)), store).ReadAsync("fictional plan"));
        Assert.Equal("fallback", store.GetSnapshot().State);
        var cancelled = new CancellationToken(true);
        var reader = new ObservedVisitScheduleReader(new ScheduleReader(Task.FromCanceled<VisitScheduleDraft>(cancelled)), store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync("fictional plan", cancelled));
        Assert.Equal("interrupted", store.GetSnapshot().State);
    }

    private sealed class OfflineInvalidProvider : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("{\"choices\":[]}", Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task StatusGetAndServerHeaderDoNotInvokeProviderOrExposePrivateConfig()
    {
        var transport = new OfflineInvalidProvider();
        using var baseline = new AppIntegrationTests.DemoFactory();
        using var factory = baseline.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                { ["AI:Provider"] = "together", ["Together:ApiKey"] = "OFFLINE-PRIVATE-KEY" }));
            builder.ConfigureServices(services => services.AddHttpClient<TogetherExtractionService>()
                .ConfigurePrimaryHttpMessageHandler(() => transport));
        });
        using var client = factory.CreateClient();
        var html = await client.GetStringAsync("/");
        Assert.Contains("AI configured", html);
        Assert.DoesNotContain("OFFLINE-PRIVATE-KEY", html);
        for (var i = 0; i < 3; i++)
        {
            using var response = await client.GetAsync("/api/ai/status");
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            Assert.Equal("unchecked", document.RootElement.GetProperty("state").GetString());
            Assert.DoesNotContain("OFFLINE-PRIVATE-KEY", json);
            Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(0, transport.Calls);
        // A provider HTTP 200 with invalid content is still a fallback, never green.
        using var read = await client.PostAsJsonAsync("/api/chat/extract", new { text = "Fictional white Volvo 760, plate 654321" });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var status = JsonDocument.Parse(await client.GetStringAsync("/api/ai/status"));
        Assert.Equal("fallback", status.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, status.RootElement.GetProperty("lastSuccessAtUtc").ValueKind);
        Assert.Equal(1, transport.Calls);
    }
}
