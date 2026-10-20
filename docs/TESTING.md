# Testing Solvia

The backend suite uses C#/xUnit. Browser smoke and journey tests use
**TypeScript and Playwright Test** with desktop Chrome and Pixel 7-sized
Chromium contexts. Tests use fictional data only.
The latest recorded validation covers **82 distinct desktop/mobile checks**,
including **30 smoke checks** and **34 SDK/error contract checks**. The earlier full run
passed 71 checks; nine targeted reruns passed after correcting test harness
expectations (eight stale copy assertions and one cold-start timeout budget).
Two new loading-feedback checks subsequently passed a targeted run in 56.8
seconds after two further harness corrections; both failed attempts are
retained. The latest 46-check main journey subset also passed. This is
aggregate coverage, not one clean 82/82 attempt. Reports remain separate.
A final clean run of all **30 smoke checks passed in 2.6 minutes** against
application source `aa6781a`. These reruns add confidence, not distinct tests.
TypeScript type checking passed; the app build completed with zero warnings
and errors.

## Backend and route regressions

```powershell
dotnet test tests/Solvia.Tests
```

The latest coordinated backend run passed **367/367 tests**, with no failures
or skips, in 34 seconds. It covers domain pricing, ownership, checkout/capture
verification, webhooks, PDF contents, actual Program.cs dependency injection,
routing, Razor rendering, antiforgery, both AI adapters, and input boundaries.
This supersedes the historical 244-test baseline and the earlier 308-test
passing run. The first expanded Meta run passed 331/332: an ambiguous
`plate might be...` sentence exposed a false plate extraction. That failure
is retained; the fix and regression, an independent review of exact vehicle
vocabulary matching, and Meta dependency-injection coverage are included in
the final passing run.

The final TRX is retained locally under ignored
`.playwright-data/backend-validation/meta-release-verified.trx`; earlier runs
remain separate. The [quality-control record](QUALITY-CONTROL.md) describes
the review, correction, and retest process and its limits.

## TypeScript browser tests

Install Node.js 20 or newer in addition to .NET 10, then run from the repo root:

```powershell
npm ci
npx playwright install chromium
npm run typecheck
npm run test:smoke
npm run test:e2e
```

On Linux CI, use `npx playwright install --with-deps chromium` for browser
system dependencies. To inspect failures or run interactively:

```powershell
npm run test:e2e:report
npm run test:e2e:ui
```

Coverage includes all main navigation pages, actual CSS/JavaScript responses,
responsive page width, campus links, AG Grid quick search and numeric sorting,
CDN failure fallback, numeric/compound plate extraction, missing-plate
validation, student/public day and term choices, employee issuance and real
PDF download, and missing-antiforgery rejection.

The suite starts its own app on **127.0.0.1:5157** with a fresh SQLite file
under ignored `.playwright-data/`. It uses the `Testing` environment, no
user-secrets, no usable PayPal or Meta credentials, an unreachable Ollama
endpoint with `AI_PROVIDER=ollama`, and a
separate build output. It does not stop or edit the user's preview at 5156.
Testing loads the source build's static-asset manifest explicitly. Report
and failure traces are ignored by Git.
Controlled test hosts explicitly clear provider credentials, including both
`META_API_KEY` and `Meta__ApiKey`; they never inherit a hosted model secret.

The real AG Grid 34.3.1 library is delivered from the pinned npm package to
the page's CDN request in tests. The main journey suite uses real application
routes and business outcomes. The separate fallback test deliberately blocks that request.
An actual CDN-loaded grid was also checked in the local preview browser.

`checkout-errors.spec.ts` separately simulates the PayPal SDK callbacks and
HTTP failure/pending responses, while using real permit and checkout pages.
It verifies that order IDs appear immediately and that failed, pending, or
unreadable capture responses stay on checkout with a visible message. These
contract tests are not evidence of a live PayPal capture.

Native PayPal/card buttons are the primary payment controls. The suite also
covers failed or stalled SDK loading/rendering, late completion after a
timeout, eligibility failure, and a protected redirect backup without a
duplicate payment action. These are controlled SDK tests. A separate pair
of checks against the actual remote SDK and JavaScript-disabled checkout
also passed; see the dated provider results below.

## Provider integration evidence and limits

The isolated Playwright suite stops before external buyer approval. Its SDK
contract tests simulate provider responses; they do not prove a provider
capture or real AI inference.

A separate check against the running app and the real PayPal **Sandbox**
completed on **2026-10-19 at 22:26:52 UTC**:

| Check | Verified result |
|---|---|
| Application | Maria, Fresno City College term permit, $30.00 USD |
| PayPal order | `1G1852006N6969449`, provider status `COMPLETED` |
| PayPal capture | `7WR24202DB5257942`, provider status `COMPLETED` |
| App capture endpoint | HTTP 200; `completed` true |
| App receipt | `SOL-2026-197973` |
| Paid permit PDF | One US Letter page; $30.00 USD and both provider IDs checked in extracted text and rendered output |

This check used PayPal's published Sandbox test card with the official
`confirm-payment-source` API, followed by the app's actual
`POST /api/paypal/capture` endpoint. An independent provider order query
matched the completed capture, exact amount, currency, and merchant. No raw
card-handling feature was added to Solvia. This was a test-money transaction.

Earlier in the same session, an unapproved order returned HTTP 409 with
`ORDER_NOT_APPROVED`, and redirect resume reused its existing approval URL.
The later completed capture supersedes that earlier pending status.

Wallet/Personal-buyer UI approval has **not** been verified. A Business
sandbox seller cannot buy from itself; use a separate **Personal** sandbox
buyer when testing that UI path. Never put credentials in test source,
reports, or commits. See [PayPal's sandbox account guide](https://developer.paypal.com/sandbox-testing/accounts).

Real application AI verification requires the selected provider's `ollama`
or `meta` source label. The demo person switcher tests fictional ownership flows and is not
production authentication. CSV pricing is the accepted hackathon scope;
no real-time college fee feed is claimed.

## Multi-college Sandbox matrix

The subsequent opt-in real-provider suite passed **20 checks**: 16 permit
cases and four payment guards. The cases cover CSU Monterey Bay, CSU East
Bay, Fresno City College, and Modesto Junior College / Columbia College /
YCCD, each with Student and Public personas buying day and term permits.
Fourteen captures completed for **$945.00 USD in Sandbox test funds**; the two
YCCD term permits correctly issued free without PayPal transactions.

Independent read-only PDF validation checked all 16 documents for one-page
US Letter output, exact campus, plate, category, permit type, fee, receipt,
and payment references. Free documents correctly state `FREE - ISSUED` and
`Not required ($0 fee / exempt)` for provider references. Selected public day,
public term, and long-campus-name free documents were also rendered for
visual review.

See [matrix setup and scope](SANDBOX-MATRIX.md) and
[the dated provider results](SANDBOX-RESULTS-2026-10-19.md). These card/API
captures do not establish that interactive PayPal wallet checkout was tested.

## Opt-in live AI checks

`tests/ai/model-live.spec.ts` contains **eight intended live-provider cases**,
separate from the controlled browser counts above. It uses the already-running
loopback app (default port 5156), creates fictional vehicle/application drafts,
and stops before any PayPal order/payment. It does not start or reconfigure
the app. Positive extraction cases require the actual selected source;
negative/ambiguous inputs may safely produce a labeled fallback.

Configure the app first using [README provider setup](../README.md#step-4--select-an-optional-ai-provider).
For Meta, the simplest route is `scripts/Set-MetaCredentials.ps1`, which reads
the key through a hidden prompt into .NET Development user-secrets. The
browser runner never receives the Meta key. Use adult (18+) fictional details
under the [provider restrictions](PRIVACY.md).

```powershell
$env:SOLVIA_RUN_LIVE_AI = '1'
$env:SOLVIA_AI_PROVIDER = 'meta'
npx playwright test --config=playwright.ai.config.ts
Remove-Item Env:SOLVIA_RUN_LIVE_AI,Env:SOLVIA_AI_PROVIDER
```

For Ollama, set `SOLVIA_AI_PROVIDER=ollama` instead and ensure the app itself
uses `AI_PROVIDER=ollama` with Phi-4-mini available. The flags identify and
authorize the test; they do not switch the app's server provider. Optional
`SOLVIA_AI_BASE_URL` must remain loopback. Meta calls may incur charges.

Evidence is written under ignored `.playwright-data/ai-evidence/{provider}/`;
traces are off. Retain failures and source labels. The eight-case suite is
prepared, **not a claim of eight passing live tests**. A warmed direct CPU
Phi-4-mini response took 32.26 seconds while the 10-second app budget fell
back. Meta has no configured key or verified live request at this checkpoint.
See [Meta's primary API reference](https://dev.meta.ai/docs/api-reference/chat-completions/schemas)
for the hosted request contract.
