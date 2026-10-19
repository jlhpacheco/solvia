using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Solvia.Config;

namespace Solvia.Services;

/// <summary>
/// The Elastic college search (BUILD-PLAN T8, stretch): the same
/// <see cref="ICollegeSearch"/> contract served by a local
/// Elasticsearch <c>colleges</c> index — one index over the 10
/// campuses, hybrid keyword <c>multi_match</c> over the campus name
/// (boosted) and a combined <c>searchText</c> field holding the fee
/// notes (see scripts/elastic/). ELSER semantic search is
/// deliberately NOT in this path: it needs an ML node / inference
/// endpoint this local setup does not run, and nothing here claims
/// it (docs/SPONSORS.md states which mode actually ran).
///
/// Honesty + safety rules:
/// - Elastic only FINDS and RANKS. Every hit's campus name is
///   resolved back through <see cref="IFeeCatalog"/>, so the rows
///   (and every price) are the catalog's — the index can never
///   invent or drift a fee.
/// - ANY failure — node down, timeout, non-2xx, malformed reply —
///   falls back to <see cref="BuiltinCollegeSearch"/> and the result
///   says ServedBy "builtin". The pick screen never dies because a
///   search server is down, and never pretends Elastic answered
///   when it didn't.
/// - Selected only when config <c>Search:Provider</c> is "elastic";
///   the shipped default stays "builtin".
/// </summary>
public sealed class ElasticCollegeSearch : ICollegeSearch
{
    public const string ProviderName = "elastic";

    private readonly HttpClient _http;
    private readonly IFeeCatalog _catalog;
    private readonly BuiltinCollegeSearch _fallback;
    private readonly ElasticOptions _options;

    public ElasticCollegeSearch(
        HttpClient http,
        IFeeCatalog catalog,
        BuiltinCollegeSearch fallback,
        IOptions<ElasticOptions> options)
    {
        _http = http;
        _catalog = catalog;
        _fallback = fallback;
        _options = options.Value;
    }

    public async Task<CollegeSearchResult> SearchAsync(
        string? query, CancellationToken cancellationToken = default)
    {
        try
        {
            var ranked = await QueryElasticAsync(query, cancellationToken);
            return new CollegeSearchResult(ranked, ProviderName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException ||
                                   !cancellationToken.IsCancellationRequested)
        {
            // Node down / timeout / bad reply: the built-in match
            // answers instead, and ServedBy tells the truth about it.
            return await _fallback.SearchAsync(query, cancellationToken);
        }
    }

    private async Task<IReadOnlyList<CampusFee>> QueryElasticAsync(
        string? query, CancellationToken cancellationToken)
    {
        object body;
        if (string.IsNullOrWhiteSpace(query))
        {
            body = new { size = 25, query = new { match_all = new { } } };
        }
        else
        {
            body = new
            {
                size = 25,
                query = new
                {
                    multi_match = new
                    {
                        query = query.Trim(),
                        fields = new[] { "campus^3", "searchText" },
                        type = "best_fields",
                    },
                },
            };
        }

        var json = JsonSerializer.Serialize(body);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(
            $"{_options.Index}/_search", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var hits = doc.RootElement
            .GetProperty("hits")
            .GetProperty("hits");

        var ranked = new List<CampusFee>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in hits.EnumerateArray())
        {
            if (!hit.TryGetProperty("_source", out var source) ||
                !source.TryGetProperty("campus", out var campusProp))
            {
                continue;
            }

            var name = campusProp.GetString();
            if (name is null || !seen.Add(name))
            {
                continue;
            }

            // Resolve through the catalog: prices and row shape are
            // the catalog's, never the index's copy.
            var fee = _catalog.Find(name);
            if (fee is not null)
            {
                ranked.Add(fee);
            }
        }

        return ranked;
    }
}
