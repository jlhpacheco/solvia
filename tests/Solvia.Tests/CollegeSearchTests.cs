using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Controllers;
using Solvia.Models.ViewModels;
using Solvia.Services;

namespace Solvia.Tests;

/// <summary>
/// R8 search-seam tests (REDO-PLAN §2e): the built-in match ranks
/// deterministically and labels itself; the Elastic provider serves
/// catalog-resolved rows when the index answers — and falls back to
/// the built-in match, LABELED "builtin", on any failure (down node,
/// HTTP error). The search page model carries whoever really served,
/// so the on-screen label is testable truth, not markup faith.
/// All Elastic traffic is a stub HttpMessageHandler — no network.
/// </summary>
public sealed class CollegeSearchTests
{
    private static FeeCatalogService Catalog() =>
        new(RedoTestDb.FindFeesCsv());

    // ------------------------------------------------------------- builtin

    [Fact]
    public async Task Builtin_EmptyQuery_ReturnsAllTen_InCatalogOrder()
    {
        var search = new BuiltinCollegeSearch(Catalog());

        var result = await search.SearchAsync(null);

        Assert.Equal(BuiltinCollegeSearch.ProviderName, result.ServedBy);
        Assert.Equal(10, result.Campuses.Count);
        Assert.Equal("CSU Monterey Bay", result.Campuses[0].Campus);
    }

    [Fact]
    public async Task Builtin_NameQuery_RanksTheNameMatchFirst()
    {
        var search = new BuiltinCollegeSearch(Catalog());

        var fresno = await search.SearchAsync("Fresno");
        Assert.Equal("Fresno City College", fresno.Campuses[0].Campus);

        var monterey = await search.SearchAsync("monterey");
        Assert.Equal("CSU Monterey Bay", monterey.Campuses[0].Campus);
    }

    [Fact]
    public async Task Builtin_FeeTextQuery_FindsCampusesByTheirNotes()
    {
        var search = new BuiltinCollegeSearch(Catalog());

        var result = await search.SearchAsync("motorcycle");

        // Several campuses publish motorcycle terms in their fee
        // notes; campuses whose motorcycle cell is "not listed"
        // never match the word.
        Assert.True(result.Campuses.Count >= 3);
        Assert.All(result.Campuses, fee =>
            Assert.Contains(fee, Catalog().All()));
    }

    [Fact]
    public async Task Builtin_NonsenseQuery_ReturnsNothing_NotEverything()
    {
        var search = new BuiltinCollegeSearch(Catalog());

        var result = await search.SearchAsync("zzzzqqqq");

        Assert.Empty(result.Campuses);
        Assert.Equal(BuiltinCollegeSearch.ProviderName, result.ServedBy);
    }

    // ------------------------------------------------------------- elastic

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
            _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private static ElasticCollegeSearch Elastic(HttpMessageHandler handler)
    {
        var catalog = Catalog();
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:9200/"),
        };
        return new ElasticCollegeSearch(
            http, catalog, new BuiltinCollegeSearch(catalog),
            Options.Create(new ElasticOptions()));
    }

    [Fact]
    public async Task Elastic_Hits_ResolveThroughCatalog_AndLabelElastic()
    {
        // The index ranks East Bay above Fresno for this query; the
        // rows that come back must be the CATALOG's records (prices
        // included), in the index's order, labeled "elastic".
        const string hitsJson = """
            { "hits": { "hits": [
                { "_source": { "campus": "CSU East Bay" } },
                { "_source": { "campus": "Fresno City College" } }
            ] } }
            """;
        var search = Elastic(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(hitsJson, System.Text.Encoding.UTF8, "application/json"),
        }));

        var result = await search.SearchAsync("california state");

        Assert.Equal(ElasticCollegeSearch.ProviderName, result.ServedBy);
        Assert.Equal(
            new[] { "CSU East Bay", "Fresno City College" },
            result.Campuses.Select(c => c.Campus).ToArray());
        // Catalog truth, not index data: East Bay's real $195 Fall fee.
        Assert.Equal(19500, result.Campuses[0].FallSemesterCents);
    }

    [Fact]
    public async Task Elastic_NodeDown_FallsBackToBuiltin_AndSaysSo()
    {
        var search = Elastic(new StubHandler(_ =>
            throw new HttpRequestException("connection refused (stub)")));

        var result = await search.SearchAsync("Fresno");

        Assert.Equal(BuiltinCollegeSearch.ProviderName, result.ServedBy);
        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
    }

    [Fact]
    public async Task Elastic_HttpError_FallsBackToBuiltin_AndSaysSo()
    {
        var search = Elastic(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await search.SearchAsync("Fresno");

        Assert.Equal(BuiltinCollegeSearch.ProviderName, result.ServedBy);
        Assert.Equal("Fresno City College", result.Campuses[0].Campus);
    }

    // --------------------------------------------------------- search page

    private sealed class StubTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    [Fact]
    public async Task SearchPage_ModelNamesTheProviderThatServed()
    {
        var (db, connection) = RedoTestDb.Create();
        using var _ = connection;
        using var __ = db;
        var catalog = Catalog();
        var context = new DefaultHttpContext();
        var controller = new CollegeController(
            catalog, new TermDatesService(catalog, db),
            new BuiltinCollegeSearch(catalog),
            new PriceRepairService(db, catalog, new PricingService(catalog)),
            Options.Create(new FeeTableOptions()),
            Options.Create(new CalendarOptions()),
            db)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
        controller.TempData = new TempDataDictionary(context, new StubTempDataProvider());

        var result = await controller.Search("Fresno", default);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<CollegeSearchViewModel>(view.Model);
        // The page renders this verbatim as "Search served by: …".
        Assert.Equal("builtin", model.ServedBy);
        Assert.Equal("Fresno City College", model.Rows[0].Campus);
        Assert.Equal("$30.00 per term", model.Rows[0].SemesterPriceLabel);
    }
}
