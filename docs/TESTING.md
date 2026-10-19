# Testing Solvia

The backend suite uses C#/xUnit. Browser smoke and journey tests use
**TypeScript and Playwright Test** with desktop Chrome and Pixel 7-sized
Chromium contexts. Tests use fictional data only.
The final October 19 full browser run passed **46/46 checks**; TypeScript
type checking passed. The app build completed with zero warnings and errors.

## Backend and route regressions

```powershell
dotnet test tests/Solvia.Tests
```

This covers domain pricing, ownership, checkout/capture verification,
webhooks, PDF contents, and actual Program.cs dependency injection, routing,
Razor rendering, and antiforgery. The October 19 run passed **231 tests**.

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

## Live integration boundary

Browser tests stop before external buyer approval. They do not prove a live
PayPal capture or a real AI inference. On October 19, the running app
successfully authenticated with PayPal Sandbox, created a $30 USD order,
and reused that order on a repeated request. Buyer approval/capture remains
pending until the Personal sandbox buyer completes it.

The follow-up real-provider check returned `CREATED` with the expected
$30 USD amount. Calling the app's capture endpoint returned HTTP 409 with
`ORDER_NOT_APPROVED`; retrying the redirect start returned 302 to the same
provider approval URL. No capture ID or completed payment was claimed.

Latest validation: 244 backend tests, 56 desktop/mobile checks in the full
browser run, followed by the four added receipt-navigation checks (two on
each viewport), all passing. TypeScript checking and the app build pass.

Use the **Personal** test account as the buyer and the app's **Business**
test account as the seller. Never put credentials in test source, reports,
or commits. See [PayPal's sandbox account guide](https://developer.paypal.com/sandbox-testing/accounts).
Real AI verification requires Ollama/Phi-4-mini and an `ollama` source label.
