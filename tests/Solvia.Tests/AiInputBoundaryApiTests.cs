using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Real API routing/binding must enforce the input bound before inference.</summary>
public sealed class AiInputBoundaryApiTests
{
    private sealed class CountingExtractor : IAiExtractor
    {
        public int Calls { get; private set; }

        public Task<ExtractionResult> ExtractAsync(string studentText, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ExtractionResult { Source = ExtractionSource.Fallback, Plate = "8XYZ123" });
        }
    }

    [Fact]
    public async Task ExtractApi_OversizedInput_IsRejectedWithoutCallingModel_ExactLimitWorks()
    {
        var extractor = new CountingExtractor();
        using var baseFactory = new AppIntegrationTests.DemoFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAiExtractor>();
            services.AddSingleton<IAiExtractor>(extractor);
        }));
        using var client = factory.CreateClient();

        var tooLong = await client.PostAsJsonAsync("/api/chat/extract", new { text = new string('x', 1001) });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("1,000 characters or fewer", await tooLong.Content.ReadAsStringAsync());
        Assert.Equal(0, extractor.Calls);

        var boundary = await client.PostAsJsonAsync("/api/chat/extract", new { text = new string('x', 1000) });

        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        Assert.Contains("8XYZ123", await boundary.Content.ReadAsStringAsync());
        Assert.Equal(1, extractor.Calls);
    }
}
