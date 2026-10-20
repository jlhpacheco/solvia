using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>Runs actual provider selection, Razor disclosure and API routing without outbound HTTP.</summary>
public sealed class AiProviderSelectionIntegrationTests
{
    [Theory]
    [InlineData("Qwen/Qwen3.5-9B")]
    [InlineData("openai/gpt-oss-20b")]
    [InlineData("unapproved-model")]
    public void StartupRejectsAnUnapprovedHostedModel_InsteadOfSilentlyChangingItsOrigin(string model)
    {
        using var baseFactory = new AppIntegrationTests.DemoFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Together:Model"] = model,
                    ["Together:ApiKey"] = ""
                })));
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("Together:Model must be openai/gpt-oss-120b", error.Message);
    }

    [Fact]
    public async Task TogetherSelectedWithoutKey_DisclosesHostedProcessing_UsesRulesWithoutCallingEitherProvider()
    {
        var togetherTransport = new NoNetworkHandler();
        var ollamaTransport = new NoNetworkHandler();
        using var baseFactory = new AppIntegrationTests.DemoFactory();
        using var factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AI:Provider"] = "together",
                    ["Together:ApiKey"] = "",
                    ["Ollama:BaseUrl"] = "http://127.0.0.1:1/"
                }));
            builder.ConfigureServices(services =>
            {
                // Clear any developer-machine credential after binding. Both
                // real adapters also have an in-memory transport as a backstop.
                services.PostConfigure<TogetherOptions>(options => options.ApiKey = "");
                services.AddHttpClient<TogetherExtractionService>()
                    .ConfigurePrimaryHttpMessageHandler(() => togetherTransport);
                services.AddHttpClient<OllamaExtractionService>()
                    .ConfigurePrimaryHttpMessageHandler(() => ollamaTransport);
            });
        });
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        Assert.IsType<TogetherExtractionService>(scope.ServiceProvider.GetRequiredService<IAiExtractor>());
        Assert.True(string.IsNullOrEmpty(scope.ServiceProvider.GetRequiredService<IOptions<TogetherOptions>>().Value.ApiKey));

        var html = await client.GetStringAsync("/Permits/Questions?college=Fresno%20City%20College");
        Assert.Contains("Use a fictional vehicle", html);
        Assert.Contains("Together", html);
        Assert.Contains("hosted AI reader", html);
        Assert.DoesNotContain("configured Ollama AI reader", html);

        var response = await client.PostAsJsonAsync("/api/chat/extract", new
        {
            text = "Fictional demo vehicle: white Volvo 760, license plate 654321"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("fallback", json.RootElement.GetProperty("source").GetString());
        Assert.Equal("654321", json.RootElement.GetProperty("plate").GetString());
        Assert.Equal("Volvo", json.RootElement.GetProperty("carMake").GetString());
        Assert.Equal(0, togetherTransport.Calls);
        Assert.Equal(0, ollamaTransport.Calls);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new HttpRequestException("Network calls are disabled in this integration test.");
        }
    }
}
