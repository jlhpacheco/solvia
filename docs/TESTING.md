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
the page's CDN request in tests. Application routes and business outcomes
are not mocked. The separate fallback test deliberately blocks that request.
An actual CDN-loaded grid was also checked in the local preview browser.

## Live integration boundary

Browser tests stop before external buyer approval. They do not prove a live
PayPal capture or a real AI inference. On October 19, the running app
successfully authenticated with PayPal Sandbox, created a $30 USD order,
and reused that order on a repeated request. Buyer approval/capture remains
pending until the Personal sandbox buyer completes it.

Use the **Personal** test account as the buyer and the app's **Business**
test account as the seller. Never put credentials in test source, reports,
or commits. See [PayPal's sandbox account guide](https://developer.paypal.com/sandbox-testing/accounts).
Real AI verification requires Ollama/Phi-4-mini and an `ollama` source label.
