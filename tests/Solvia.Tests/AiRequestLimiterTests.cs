using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Solvia.Controllers;
using Solvia.Services;

namespace Solvia.Tests;

public sealed class AiRequestLimiterTests
{
    [Fact]
    public void ClientQuota_RejectsUntilMonotonicWindowExpires()
    {
        var clock = new ManualClock();
        var limiter = new AiRequestLimiter(clock);
        for (var i = 0; i < AiRequestLimiter.MaxRequestsPerClient; i++)
        {
            Assert.True(limiter.TryAcquire("one", out var lease, out _));
            lease!.Dispose();
        }
        Assert.False(limiter.TryAcquire("one", out var rejected, out var retry));
        Assert.Null(rejected);
        Assert.Equal(60, retry);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.False(limiter.TryAcquire("one", out _, out retry));
        Assert.Equal(1, retry);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(limiter.TryAcquire("one", out var next, out _));
        next!.Dispose();
    }

    [Fact]
    public void ConcurrentLimits_ArePerClientAndGlobal_AndLeaseReleasesOnlyOnce()
    {
        var limiter = new AiRequestLimiter();
        var leases = new List<IDisposable>();
        for (var i = 0; i < AiRequestLimiter.MaxConcurrentRequests; i++)
        {
            Assert.True(limiter.TryAcquire(i < 2 ? "one" : "two", out var lease, out _));
            leases.Add(lease!);
        }
        Assert.False(limiter.TryAcquire("one", out _, out var perClientRetry));
        Assert.Equal(1, perClientRetry);
        Assert.False(limiter.TryAcquire("three", out _, out _));
        leases[0].Dispose();
        leases[0].Dispose();
        Assert.True(limiter.TryAcquire("three", out var replacement, out _));
        Assert.False(limiter.TryAcquire("four", out _, out _));
        replacement!.Dispose();
        foreach (var lease in leases) lease.Dispose();
    }

    [Fact]
    public void RotatingClients_CannotBypassGlobalMinuteQuota()
    {
        var clock = new ManualClock();
        var limiter = new AiRequestLimiter(clock);
        for (var i = 0; i < AiRequestLimiter.MaxRequestsGlobally; i++)
        {
            Assert.True(limiter.TryAcquire($"client-{i}", out var lease, out _));
            lease!.Dispose();
        }
        Assert.False(limiter.TryAcquire("another-client", out _, out var retry));
        Assert.Equal(60, retry);
        clock.Advance(AiRequestLimiter.WindowLength);
        Assert.True(limiter.TryAcquire("another-client", out var next, out _));
        next!.Dispose();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancellation")]
    public async Task Controller_ReleasesConcurrencyAdmission_OnEveryModelOutcome(string outcome)
    {
        const string address = "203.0.113.10";
        var limiter = new AiRequestLimiter();
        Assert.True(limiter.TryAcquire(address, out var held, out _));
        using var first = held;
        using var cancellation = new CancellationTokenSource();
        var extractor = new RecordingExtractor((_, token) => outcome switch
        {
            "failure" => Task.FromException<ExtractionResult>(new InvalidOperationException("offline test")),
            "cancellation" => CancelDuringInference(cancellation),
            _ => Task.FromResult(Answer()),
        });
        var controller = new ChatController(extractor, NullLogger<ChatController>.Instance, limiter)
        {
            ControllerContext = new ControllerContext { HttpContext = Context(address) },
        };

        if (outcome == "success")
            Assert.IsType<OkObjectResult>(await controller.Extract(new("white Volvo, plate654321"), cancellation.Token));
        else if (outcome == "failure")
            await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Extract(new("white Volvo, plate654321"), cancellation.Token));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Extract(new("white Volvo, plate654321"), cancellation.Token));

        Assert.Equal(1, extractor.Calls);
        Assert.True(limiter.TryAcquire(address, out var releasedSlot, out _));
        releasedSlot!.Dispose();
    }

    [Fact]
    public async Task AlreadyCanceledRequest_DoesNotInvokeModelOrConsumeAdmission()
    {
        const string address = "203.0.113.20";
        var limiter = new AiRequestLimiter();
        var extractor = new RecordingExtractor();
        var controller = new ChatController(extractor, NullLogger<ChatController>.Instance, limiter)
        {
            ControllerContext = new ControllerContext { HttpContext = Context(address) },
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.Extract(new("white Volvo, plate654321"), cancellation.Token));
        Assert.Equal(0, extractor.Calls);
        for (var i = 0; i < AiRequestLimiter.MaxRequestsPerClient; i++)
        {
            Assert.True(limiter.TryAcquire(address, out var lease, out _));
            lease!.Dispose();
        }
    }

    [Fact]
    public async Task RealRoutes_ShareQuotaBeforeModel_AndKeepMvcAnswersOn429()
    {
        var extractor = new RecordingExtractor();
        var limiter = new AiRequestLimiter(new ManualClock());
        using var original = new AppIntegrationTests.DemoFactory();
        using var factory = original.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAiExtractor>();
            services.AddSingleton<IAiExtractor>(extractor);
            services.RemoveAll<AiRequestLimiter>();
            services.AddSingleton(limiter);
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var start = await client.GetAsync("/Permits/Questions?college=Fresno%20City%20College");
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.NotNull(start.Headers.Location);
        Assert.Contains("flowId=", start.Headers.Location!.ToString());
        var page = await client.GetStringAsync(start.Headers.Location);
        var token = WebUtility.HtmlDecode(Regex.Match(page,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        Assert.True(Guid.TryParse(Regex.Match(page, "name=\"flowId\"[^>]*value=\"([^\"]+)\"").Groups[1].Value,
            out var flowId));
        Assert.NotEqual(Guid.Empty, flowId);
        var draftOwner = Regex.Match(page, "data-draft-owner=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(draftOwner);
        const string sentence = "Fictional white Volvo760, license plate654321, CA";

        for (var i = 0; i < AiRequestLimiter.MaxRequestsPerClient; i++)
        {
            // A changed description requests real extraction. Re-reading the
            // unchanged owner-bound draft intentionally reuses reviewed facts
            // and must not be mistaken for another metered model call.
            var changedSentence = $"{sentence}; fictional description revision {i}";
            using var response = i % 2 == 0
                ? await client.PostAsync("/api/chat/extract", Json(new { text = changedSentence }))
                : await client.PostAsync("/Permits/Understand", Form(token, flowId, changedSentence));
            Assert.Equal(i % 2 == 0 ? HttpStatusCode.OK : HttpStatusCode.Redirect, response.StatusCode);
            if (i % 2 != 0)
            {
                Assert.NotNull(response.Headers.Location);
                Assert.Contains("/Permits/Understood", response.Headers.Location!.ToString());
                Assert.Contains(flowId.ToString(), response.Headers.Location.ToString());
                using var understood = await client.GetAsync(response.Headers.Location);
                Assert.Equal(HttpStatusCode.OK, understood.StatusCode);
            }
            Assert.Equal(i + 1, extractor.Calls);
        }

        using var api = await client.PostAsync("/api/chat/extract", Json(new { text = sentence }));
        Assert.Equal(HttpStatusCode.TooManyRequests, api.StatusCode);
        Assert.Equal("60", api.Headers.GetValues("Retry-After").Single());
        Assert.Contains(AiRequestLimiter.RejectionMessage, await api.Content.ReadAsStringAsync());
        Assert.Equal(AiRequestLimiter.MaxRequestsPerClient, extractor.Calls);
        using var mvc = await client.PostAsync("/Permits/Understand", Form(token, flowId, sentence));
        Assert.Equal(HttpStatusCode.TooManyRequests, mvc.StatusCode);
        Assert.Equal("60", mvc.Headers.GetValues("Retry-After").Single());
        var html = await mvc.Content.ReadAsStringAsync();
        Assert.Contains(sentence, html);
        Assert.Contains("Your answers are still here.", html);
        Assert.Contains("Fresno City College", html);
        Assert.Contains($"value=\"{flowId}\"", html);
        Assert.Contains($"data-draft-owner=\"{draftOwner}\"", html);
        Assert.Contains("checked", Regex.Match(html, "<input\\b[^>]*id=\"typeDaily\"[^>]*>").Value);
        Assert.Contains("checked", Regex.Match(html, "<input\\b[^>]*id=\"termSummer\"[^>]*>").Value);
        Assert.Equal(AiRequestLimiter.MaxRequestsPerClient, extractor.Calls);
    }

    private static StringContent Json(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private static FormUrlEncodedContent Form(string token, Guid flowId, string sentence) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = token,
        ["flowId"] = flowId.ToString(),
        ["college"] = "Fresno City College",
        ["carText"] = sentence,
        ["permitType"] = "Daily",
        ["term"] = "Summer",
    });

    private static DefaultHttpContext Context(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return context;
    }

    private static Task<ExtractionResult> CancelDuringInference(CancellationTokenSource cancellation)
    {
        cancellation.Cancel();
        return Task.FromCanceled<ExtractionResult>(cancellation.Token);
    }

    private static ExtractionResult Answer() => new() { Plate = "654321", Source = ExtractionSource.Fallback };

    private sealed class RecordingExtractor(Func<string, CancellationToken, Task<ExtractionResult>>? response = null) : IAiExtractor
    {
        public int Calls;
        public Task<ExtractionResult> ExtractAsync(string studentText, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return response?.Invoke(studentText, cancellationToken) ?? Task.FromResult(Answer());
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public void Advance(TimeSpan amount) => _timestamp += amount.Ticks;
    }
}
