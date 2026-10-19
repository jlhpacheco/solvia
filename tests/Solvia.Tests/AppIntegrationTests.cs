using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Solvia.Data;

namespace Solvia.Tests;

/// <summary>Runs Program.cs, its real DI graph, routing, Razor, and antiforgery.</summary>
public sealed class AppIntegrationTests : IClassFixture<AppIntegrationTests.DemoFactory>
{
    private readonly HttpClient _client;
    public AppIntegrationTests(DemoFactory factory) => _client = factory.CreateClient();

    [Theory]
    [InlineData("/")]
    [InlineData("/Permits/Apply")]
    [InlineData("/Permits/Ask")]
    [InlineData("/Permits/My")]
    [InlineData("/Vehicles")]
    [InlineData("/College/Fees")]
    [InlineData("/College/Dates")]
    [InlineData("/College/Roster")]
    [InlineData("/College/Search?q=Fresno")]
    [InlineData("/verify")]
    public async Task PublicDemoPages_RenderThroughRealApplication(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("unhandled exception", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FeePage_IncludesSponsorGridAndPublishedCampusData()
    {
        var html = await _client.GetStringAsync("/College/Fees");
        Assert.Contains("ag-grid-community@34.3.1", html);
        Assert.Contains("Fresno City College", html);
        Assert.Contains("feeGrid", html);
    }

    [Fact]
    public async Task MutatingEndpoints_RejectRequestsWithoutAntiforgeryToken()
    {
        var result = await _client.PostAsync("/Home/SwitchStudent", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = "elena" }));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        result = await _client.PostAsync("/api/paypal/orders", new StringContent("{\"permitApplicationId\":\"00000000-0000-0000-0000-000000000001\"}", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }

    public sealed class DemoFactory : WebApplicationFactory<Program>
    {
        private readonly string _database = Path.Combine(Path.GetTempPath(), $"solvia-integration-{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var root = Path.GetDirectoryName(RedoTestDb.FindFeesCsv())!;
            var repo = Path.GetDirectoryName(root)!;
            builder.UseContentRoot(Path.Combine(repo, "src", "Solvia"));
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:SolviaDb"] = $"Data Source={_database}",
                ["FeesCsvPath"] = RedoTestDb.FindFeesCsv(),
                ["PayPal:ClientId"] = "",
                ["PayPal:ClientSecret"] = "",
                ["Webhook:Url"] = "",
                ["Logging:LogLevel:Default"] = "Error"
            }));
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                foreach (var path in new[] { _database, _database + "-wal", _database + "-shm" })
                    if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
