namespace Solvia.Config;

/// <summary>
/// PayPal configuration section. ClientId and ClientSecret are secrets:
/// they are supplied ONLY through environment variables or .NET
/// user-secrets — never through files committed to this repo.
/// </summary>
public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string Environment { get; set; } = "sandbox";
    public string BaseUrl { get; set; } = "https://api-m.sandbox.paypal.com";
}

/// <summary>
/// Ollama configuration section — the local model runner. Solvia's AI
/// (Microsoft Phi-4-mini) runs 100% on the local machine; student data
/// never leaves the computer.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "phi4-mini";
}

/// <summary>
/// FeeTable configuration section — which fee-table renderer ships
/// (the T6 seam: "aggrid" for AG Grid Community, or "native").
/// </summary>
public sealed class FeeTableOptions
{
    public const string SectionName = "FeeTable";

    public string Renderer { get; set; } = "native";
}

/// <summary>
/// Calendar configuration section — which term-dates renderer ships
/// (the calendar seam, same flag pattern as <see cref="FeeTableOptions"/>).
/// "native" ships: Bryntum Calendar has NO free community edition — only
/// a paid license or a 45-day trial — and a trial started at build time
/// would expire during judging (Dec 1–15, 2026). "bryntum" is the
/// drop-in point for a licensed build; see docs/SPONSORS.md.
/// </summary>
public sealed class CalendarOptions
{
    public const string SectionName = "Calendar";

    public string Renderer { get; set; } = "native";
}

/// <summary>
/// Search configuration section — which college-search provider serves
/// the pick-your-college screen (the T8 seam, same flag pattern as
/// <see cref="FeeTableOptions"/>). "builtin" ships and is the default:
/// the deterministic in-memory match over the 10 fee rows needs no
/// server at all. "elastic" routes the same screen through a local
/// Elasticsearch <c>colleges</c> index (see <see cref="ElasticOptions"/>
/// and scripts/elastic/) and falls back to the built-in match whenever
/// Elastic is unreachable — the screen never dies because a search
/// server is down. docs/SPONSORS.md states which mode actually ran.
/// </summary>
public sealed class SearchOptions
{
    public const string SectionName = "Search";

    public string Provider { get; set; } = "builtin";
}

/// <summary>
/// Elastic configuration section — where the local Elasticsearch node
/// lives when <see cref="SearchOptions.Provider"/> is "elastic".
/// No credentials: a local single-node dev cluster with security off
/// (see scripts/elastic/README.md). ELSER semantic search is NOT part
/// of the default setup — it needs an ML node / inference endpoint;
/// the shipped query is a hybrid keyword multi_match.
/// </summary>
public sealed class ElasticOptions
{
    public const string SectionName = "Elastic";

    public string Url { get; set; } = "http://localhost:9200";
    public string Index { get; set; } = "colleges";
}

/// <summary>
/// Outbound payment-complete webhook (BUILD-PLAN T7 — the Zapier seam).
/// When a permit goes live (paid, or free-issued), Solvia POSTs a small
/// JSON event to <see cref="Url"/> — built for a Zapier "Catch Hook"
/// trigger (see docs/zapier-recipe.md). Empty (the default) = disabled:
/// no call is made. The URL is an endpoint address, not a credential,
/// but it is still supplied per-machine (config / WEBHOOK_URL env var),
/// never baked into code. Events carry NO student PII — permit facts
/// only (number, campus, amount, capture id, time).
/// </summary>
public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    public string Url { get; set; } = "";
}

/// <summary>Root-level application settings.</summary>
public sealed class SolviaOptions
{
    /// <summary>Path to the college fee data file (data/fees.csv).</summary>
    public string FeesCsvPath { get; set; } = "data/fees.csv";
}
