using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// T8 college-search tests — fully OFFLINE. The built-in provider is
/// exercised against the real data/fees.csv catalog; the Elastic
/// provider is exercised against a fake <see cref="HttpMessageHandler"/>
/// playing a local Elasticsearch node (canned hits, a thrown error,
/// a 503, garbage JSON). What is proven:
/// - "Fresno" ranks Fresno City College first through BOTH providers,
///   and T3's Maria sentence returns the same Fresno City row
///   through the Elastic path (the plan's T8 done-test);
/// - Elastic hits are resolved back through the catalog (the index
///   ranks, the catalog owns the rows and prices);
/// - ANY Elastic failure falls back to the built-in match and the
///   result's ServedBy says so — never a silent fake "elastic".
/// A live node is NOT required by any test here; the live run (or
/// its honest absence) is recorded in docs/SPONSORS.md.
/// </summary>
public sealed class CollegeSearchTests
{
    private const string MariaSentence =
        "I study at Fresno City College, my car is a blue 2019 Honda Civic, " +
        "plate 8XYZ123, I need the fall term";

    private static readonly FeeCatalogService Catalog = new(FindFeesCsv());

    private static BuiltinCollegeSearch Builtin => new(Catalog);

    // ------------------------------------------------------------------
    // Built-in provider
    // ------------------------------------------------------------------

    [Fact]
    public async Task Builtin_FresnoQuery_FresnoCityCollegeFirst()
    {
        var result = await Builtin.SearchAsync("Fresno");

        Assert.Equal("builtin", result.ServedBy);
        Assert.NotEmpty(result.Campuses);
        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
        Assert.Equal(3000, result.Campuses[0].FallSemesterCents);
    }

    [Fact]
    public async Task Builtin_MariaSentence_FresnoCityCollegeFirst()
    {
        var result = await Builtin.SearchAsync(MariaSentence);

        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
    }

    [Fact]
    public async Task Builtin_EmptyQuery_ReturnsAll25InCatalogOrder()
    {
        var result = await Builtin.SearchAsync("  ");

        Assert.Equal(25, result.Campuses.Count);
        Assert.Equal(
            Catalog.All().Select(c => c.Campus),
            result.Campuses.Select(c => c.Campus));
    }

    [Fact]
    public async Task Builtin_MontereyQuery_CsuMontereyBayFirst()
    {
        var result = await Builtin.SearchAsync("monterey");

        Assert.Equal("CSU Monterey Bay", result.Campuses[0].Campus);
        Assert.Equal(22950, result.Campuses[0].FallSemesterCents);
    }

    [Fact]
    public async Task Builtin_NoMatch_ReturnsEmpty()
    {
        var result = await Builtin.SearchAsync("zzzzqq nonexistent");

        Assert.Empty(result.Campuses);
        Assert.Equal("builtin", result.ServedBy);
    }

    // ------------------------------------------------------------------
    // Elastic provider (fake node)
    // ------------------------------------------------------------------

    private const string FresnoHitsJson = """
        {
          "took": 3,
          "hits": {
            "total": { "value": 2, "relation": "eq" },
            "hits": [
              { "_id": "fresno-city-college", "_score": 12.31,
                "_source": { "campus": "Fresno City College" } },
              { "_id": "state-center-community-college-district", "_score": 4.07,
                "_source": { "campus": "State Center Community College District" } }
            ]
          }
        }
        """;

    private static ElasticCollegeSearch MakeElastic(FakeElasticHandler handler)
    {
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:9200/"),
        };
        return new ElasticCollegeSearch(
            http, Catalog, Builtin, Options.Create(new ElasticOptions()));
    }

    [Fact]
    public async Task Elastic_FakeHits_CatalogRowsInHitOrder_ServedByElastic()
    {
        var handler = new FakeElasticHandler { ResponseBody = FresnoHitsJson };
        var search = MakeElastic(handler);

        var result = await search.SearchAsync("Fresno");

        Assert.Equal("elastic", result.ServedBy);
        Assert.Equal(
            ["Fresno City College", "State Center Community College District"],
            result.Campuses.Select(c => c.Campus).ToArray());
        // Prices come from the catalog, not the index document.
        Assert.Equal(3000, result.Campuses[0].FallSemesterCents);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("/colleges/_search", request.Path);
        Assert.Contains("multi_match", request.Body);
        Assert.Contains("Fresno", request.Body);
    }

    [Fact]
    public async Task Elastic_MariaSentence_SameFresnoCityRow()
    {
        var handler = new FakeElasticHandler { ResponseBody = FresnoHitsJson };
        var search = MakeElastic(handler);

        var result = await search.SearchAsync(MariaSentence);

        Assert.Equal("elastic", result.ServedBy);
        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
        Assert.Equal(3000, result.Campuses[0].FallSemesterCents);
    }

    [Fact]
    public async Task Elastic_NodeDown_FallsBackToBuiltin()
    {
        var handler = new FakeElasticHandler { Throw = true };
        var search = MakeElastic(handler);

        var result = await search.SearchAsync("Fresno");

        Assert.Equal("builtin", result.ServedBy);
        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
    }

    [Fact]
    public async Task Elastic_ServerError_FallsBackToBuiltin()
    {
        var handler = new FakeElasticHandler
        {
            Status = HttpStatusCode.ServiceUnavailable,
            ResponseBody = """{ "error": { "type": "unavailable" } }""",
        };
        var search = MakeElastic(handler);

        var result = await search.SearchAsync("Fresno");

        Assert.Equal("builtin", result.ServedBy);
        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
    }

    [Fact]
    public async Task Elastic_GarbageReply_FallsBackToBuiltin()
    {
        var handler = new FakeElasticHandler { ResponseBody = "not json at all" };
        var search = MakeElastic(handler);

        var result = await search.SearchAsync("Fresno");

        Assert.Equal("builtin", result.ServedBy);
        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// An offline stand-in for a local Elasticsearch node: records
    /// every request and answers with a canned body/status, or throws
    /// the way a dead node presents to HttpClient.
    /// </summary>
    private sealed class FakeElasticHandler : HttpMessageHandler
    {
        public sealed record RecordedRequest(string Method, string Path, string? Body);

        public List<RecordedRequest> Requests { get; } = [];

        public string ResponseBody { get; set; } = "{}";

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public bool Throw { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                body));

            if (Throw)
            {
                throw new HttpRequestException("Connection refused (fake node down).");
            }

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string FindFeesCsv()
    {
        var starts = new[]
        {
            Path.GetDirectoryName(typeof(CollegeSearchTests).Assembly.Location),
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
            "data/fees.csv not found above the test assembly.");
    }
}
