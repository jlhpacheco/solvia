# Testing Solvia

The backend suite uses C#/xUnit. Browser smoke and journey tests use
**TypeScript and Playwright Test** with desktop Chrome and Pixel 7-sized
Chromium contexts. Tests use fictional data only.
The October 19 validation passed **60 desktop/mobile checks** (56 in the full
browser run and four added receipt-navigation checks). A fresh **28-check
desktop/mobile smoke run** also passed after the branding update. TypeScript
type checking passed; the app build completed with zero warnings and errors.

## Backend and route regressions

```powershell
dotnet test tests/Solvia.Tests
```

This covers domain pricing, ownership, checkout/capture verification,
webhooks, PDF contents, and actual Program.cs dependency injection, routing,
Razor rendering, and antiforgery. The October 19 run passed **244 tests**.

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
user-secrets, no usable PayPal credentials, a disabled model endpoint, and a
separate build output. It does not stop or edit the user's preview at 5156.
Testing loads the source build's static-asset manifest explicitly. Report
and failure traces are ignored by Git.

The real AG Grid 34.3.1 library is delivered from the pinned npm package to
the page's CDN request in tests. The main journey suite uses real application
routes and business outcomes. The separate fallback test deliberately blocks that request.
An actual CDN-loaded grid was also checked in the local preview browser.

`checkout-errors.spec.ts` separately simulates the PayPal SDK callbacks and
HTTP failure/pending responses, while using real permit and checkout pages.
It verifies that order IDs appear immediately and that failed, pending, or
unreadable capture responses stay on checkout with a visible message. These
contract tests are not evidence of a live PayPal capture.

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

Real AI verification still requires Ollama/Phi-4-mini and an `ollama` source
label. The demo person switcher tests fictional ownership flows and is not
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
