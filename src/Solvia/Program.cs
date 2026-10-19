using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Solvia.Config;
using Solvia.Data;
using Solvia.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ------------------------------------------------------------
// Secrets (the PayPal client id and secret) come ONLY from environment
// variables or .NET user-secrets — never from files committed to this repo.
//
// The bridge below lets hosts that use .env-style variable names
// (PAYPAL_CLIENT_ID, OLLAMA_MODEL, ...) feed the PayPal:/Ollama: config
// sections. The standard double-underscore style (PayPal__ClientId) also
// works through the default environment-variables configuration source.
var envBridge = new Dictionary<string, string?>();
foreach (var (envName, configKey) in new[]
{
    ("PAYPAL_CLIENT_ID", "PayPal:ClientId"),
    ("PAYPAL_CLIENT_SECRET", "PayPal:ClientSecret"),
    ("PAYPAL_ENVIRONMENT", "PayPal:Environment"),
    ("PAYPAL_BASE_URL", "PayPal:BaseUrl"),
    ("OLLAMA_BASE_URL", "Ollama:BaseUrl"),
    ("OLLAMA_MODEL", "Ollama:Model"),
    ("FEES_CSV_PATH", "FeesCsvPath"),
    ("WEBHOOK_URL", "Webhook:Url"),
})
{
    var value = Environment.GetEnvironmentVariable(envName);
    if (!string.IsNullOrWhiteSpace(value))
    {
        envBridge[configKey] = value;
    }
}

if (envBridge.Count > 0)
{
    builder.Configuration.AddInMemoryCollection(envBridge);
}

// Options binding (non-secret defaults live in the option classes and
// in appsettings.json).
builder.Services.Configure<PayPalOptions>(
    builder.Configuration.GetSection(PayPalOptions.SectionName));
builder.Services.Configure<OllamaOptions>(
    builder.Configuration.GetSection(OllamaOptions.SectionName));
builder.Services.Configure<FeeTableOptions>(
    builder.Configuration.GetSection(FeeTableOptions.SectionName));
builder.Services.Configure<CalendarOptions>(
    builder.Configuration.GetSection(CalendarOptions.SectionName));
builder.Services.Configure<WebhookOptions>(
    builder.Configuration.GetSection(WebhookOptions.SectionName));
builder.Services.Configure<SolviaOptions>(builder.Configuration);

// Data: EF Core + SQLite — a single local file, zero server install.
// (BUILD-PLAN §1: the reference app's PostgreSQL provider is swapped for
// SQLite so a judge's clean install can never fail on a database server.
// Entity shapes, conversions, and indexes are unaffected by the swap.)
builder.Services.AddDbContext<SolviaDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("SolviaDb")));

// Fee catalog (BUILD-PLAN §2 contract, owned by T1; registration wired
// under T4's Program.cs allowance): the single source of prices in the
// app, loaded once from data/fees.csv. The configured FeesCsvPath is
// repo-relative ("data/fees.csv") and the content root is src/Solvia,
// so the absolute path resolves two levels up to the repo root; an
// absolute FeesCsvPath (e.g. from FEES_CSV_PATH) wins as-is.
builder.Services.AddSingleton<IFeeCatalog>(sp =>
{
    var solvia = sp.GetRequiredService<IOptions<SolviaOptions>>().Value;
    var feesPath = Path.GetFullPath(Path.Combine(
        builder.Environment.ContentRootPath, "..", "..", solvia.FeesCsvPath));
    return new FeeCatalogService(feesPath);
});

// Term-dates service (T6 calendar seam).
builder.Services.AddScoped<ITermDatesService, TermDatesService>();

// PayPal sandbox (T4): a typed HttpClient against the sandbox base URL.
// The client id/secret ride along in PayPalOptions only (env vars /
// user-secrets) — PayPalService reads them from there, uses them only
// for the token call's Basic auth, and never logs them.
builder.Services.AddHttpClient<IPayPalService, PayPalService>((sp, client) =>
{
    var payPal = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
    client.BaseAddress = new Uri(payPal.BaseUrl.TrimEnd('/') + "/");
});
builder.Services.AddScoped<PermitNumberService>();
builder.Services.AddScoped<CheckoutService>();

// Printable permit PDF (QuestPDF): the service reads the live
// request (via IHttpContextAccessor) to build the public verify
// URL printed on the permit — the same shape as the reference
// app's receipt-PDF service.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<PermitPdfService>();

// Payment-complete webhook (T7 — the Zapier seam): a typed HttpClient
// for PaymentWebhookService. It POSTs one small JSON event when a
// permit goes live, to the configured Webhook:Url only — empty URL
// (the default) means disabled and no call is ever made. The service
// never throws, so a dead endpoint can never block a payment.
builder.Services.AddHttpClient<PaymentWebhookService>();

// AI extraction (T3): a typed HttpClient against the local Ollama
// runner. Phi-4-mini runs 100% on this machine — the only call this
// client ever makes is to Ollama on localhost, so student answers
// never leave the computer. OllamaExtractionService caps each call at
// 10 s and lands in FallbackExtractor on ANY failure (down, timeout,
// invalid answer), so extraction always answers. The fallback is also
// registered on its own for direct use and tests.
builder.Services.AddSingleton<FallbackExtractor>();
builder.Services.AddHttpClient<IAiExtractor, OllamaExtractionService>((sp, client) =>
{
    var ollama = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(ollama.BaseUrl.TrimEnd('/') + "/");
});

// College search (T8 seam): the built-in deterministic match is the
// shipped default (Search:Provider=builtin) and the Elastic
// provider's fallback; Search:Provider=elastic routes the
// pick-your-college screen through a local Elasticsearch `colleges`
// index instead (scripts/elastic/). The Elastic typed client gets a
// short timeout — a down node must fail fast into the fallback,
// never hang the screen.
builder.Services.Configure<SearchOptions>(
    builder.Configuration.GetSection(SearchOptions.SectionName));
builder.Services.Configure<ElasticOptions>(
    builder.Configuration.GetSection(ElasticOptions.SectionName));
builder.Services.AddSingleton<BuiltinCollegeSearch>();
builder.Services.AddHttpClient<ElasticCollegeSearch>((sp, client) =>
{
    var elastic = sp.GetRequiredService<IOptions<ElasticOptions>>().Value;
    client.BaseAddress = new Uri(elastic.Url.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(3);
});
builder.Services.AddSingleton<ICollegeSearch>(sp =>
{
    var search = sp.GetRequiredService<IOptions<SearchOptions>>().Value;
    return string.Equals(search.Provider, ElasticCollegeSearch.ProviderName,
               StringComparison.OrdinalIgnoreCase)
        ? sp.GetRequiredService<ElasticCollegeSearch>()
        : sp.GetRequiredService<BuiltinCollegeSearch>();
});

// Add services to the container.
builder.Services.AddControllersWithViews();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// First run: create the SQLite file with T1's schema + seed data
// (permit types + the fictional students). EnsureCreated is a no-op
// once the tables exist, and this local demo database has no
// migrations to honor — without this, every data page 500s on a
// fresh machine with "no such table".
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SolviaDbContext>();
    db.Database.EnsureCreated();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
