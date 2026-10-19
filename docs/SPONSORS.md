# Solvia — sponsor pieces: what actually shipped

Honest record for the redo tree: a sponsor is claimed only where a
real piece of it runs in this app, or a real artifact for it ships in
this repo. This file is the claims source of truth for the submission.
Where a thing did **not** run, was **not** deployed, or is an account
step for a person — not code — this file says so in those words, and
no other Solvia document claims more than this file does.

Solvia is a .NET 10 ASP.NET Core MVC app (`src/Solvia/Solvia.csproj`,
`net10.0`). Its core payment flow is PayPal sandbox (see
`docs/apimatic-context.md` for the API surface).

## AG Grid — SHIPPED (fee table)

- The college-side fee table (`/College/Fees`, rendered through
  `src/Solvia/Views/Shared/_FeeTable.cshtml`) runs **AG Grid
  Community edition, v34.3.1**, loaded from its official CDN:
  `cdn.jsdelivr.net/npm/ag-grid-community@34.3.1` (the stylesheet,
  theme, and `ag-grid-community.min.js` URLs in `_FeeTable.cshtml`
  all carry `@34.3.1`).
- Community is the **free edition**: no license key, no trial. Solvia
  deliberately never uses an AG Grid trial license — a trial started
  at build time would expire before judging ends.
- The only AG Grid integration point in the codebase is
  `src/Solvia/wwwroot/js/feetable.aggrid.js`. All 10 campuses in
  `data/fees.csv` render through it, sortable and filterable, with
  prices taken exactly from `IFeeCatalog` (null = "Not published",
  FREE = "$0 FREE"). The shipped default is `FeeTable:Renderer` =
  `aggrid` (`src/Solvia/appsettings.json`).
- Fallback behind the same seam: set config `FeeTable:Renderer=native`
  and the identical rows render as a plain server-rendered table in
  `_FeeTable.cshtml` (`src/Solvia/wwwroot/js/feetable.js` is the
  native client renderer) — no other code change, same values. If
  the CDN is unreachable in AG Grid mode, the adapter in
  `feetable.aggrid.js` also falls back to that native renderer
  automatically.

## Bryntum Calendar — OUT (not shipped, no claim anywhere)

- **What ships instead:** the term dates render through Solvia's
  native calendar (`src/Solvia/wwwroot/js/calendar.js`) on
  `/College/Dates`, fed by the same `TermDates` JSON (campus, term,
  year, term start/end, permit end = term start + DurationDays,
  renewal date) that is also served at `/College/DatesJson`. No
  Bryntum code, trial or otherwise, is embedded anywhere in this app.
- **Why — the exact reason:** Bryntum has **no free edition**.
  Verified on Bryntum's own site: Bryntum Calendar is available only
  as a paid license (from $600/developer) or a **45-day trial**. A
  trial started at build time (mid-October 2026) would expire in
  early December — **in the middle of judging (Dec 1–15, 2026)** —
  leaving judges a dead or watermarked calendar. A calendar that
  dies mid-judging is worse than a clean native one, so the trial
  was never started and **no shipped-Bryntum wording appears
  anywhere in this submission**.
- **The drop-in seam (only if a licensed Bryntum build is ever
  used):**
  1. Add `wwwroot/js/calendar.bryntum.js` consuming the `TermDates`
     JSON island (`#term-dates-data` on `/College/Dates`, same shape
     as `/College/DatesJson`).
  2. Set config `Calendar:Renderer` to `bryntum` (bound by
     `CalendarOptions` in `src/Solvia/Config/AppOptions.cs`, wired in
     `Program.cs`; `CollegeController.Dates` passes the flag to the
     view).
  3. `src/Solvia/Views/College/Dates.cshtml` already switches the
     script include on that flag — no controller or data changes are
     needed.
  Until a real license exists, the shipped default stays `native`
  (`src/Solvia/appsettings.json`: `Calendar:Renderer` = `native`)
  and the seam stays unused.

## Elastic — SEAM SHIPPED; LIVE NODE NEVER RAN (honestly)

- **What shipped:** college search sits behind one contract,
  `ICollegeSearch` (`src/Solvia/Services/ICollegeSearch.cs`). The
  built-in deterministic match (`src/Solvia/Services/BuiltinCollegeSearch.cs`)
  is the default and always works. `src/Solvia/Services/ElasticCollegeSearch.cs`
  implements the same contract against a local Elasticsearch
  `colleges` index (hybrid keyword `multi_match` over `campus^3` +
  `searchText`), selected only by config `Search:Provider=elastic`
  (`Elastic:Url` default `http://localhost:9200`, `Elastic:Index`
  default `colleges` — `ElasticOptions` in
  `src/Solvia/Config/AppOptions.cs`). The find-your-college screen
  (`/College/Search?q=…`) runs through the seam and **prints which
  provider served the results** (`ServedBy`: `builtin` or `elastic`,
  in `src/Solvia/Views/College/Search.cshtml`); on any Elastic
  failure it falls back to the built-in match and the screen says
  so. Elastic only finds and ranks — every hit is resolved back
  through `IFeeCatalog`, so prices are always the catalog's, never
  the index's.
- Index mapping, an optional ELSER mapping, a seed script that loads
  all 10 campuses from `data/fees.csv`, and run instructions ship as
  artifacts of this repo in `scripts/elastic/` (mapping, optional
  ELSER mapping, seed script, README). Tests for both providers,
  including Elastic hits through a fake handler and fallback modes,
  live in `tests/Solvia.Tests/CollegeSearchTests.cs`.
- **What did NOT run, stated plainly:** **no live Elasticsearch node
  ever ran** for this build. The build sandbox has no Docker; the
  official Elasticsearch tarball is ~648 MB (≈647,847,324 bytes),
  which at the sandbox's measured speed (~0.9 MB/s) is ~12 minutes
  of download alone; and the sandbox fences app-process TCP to
  localhost, so the app could not have reached a sandbox-local node
  end-to-end in any case. The submitted demo's search is therefore
  the **built-in provider**, and **no "powered by Elasticsearch"
  claim is made**. On a normal machine the steps in
  `scripts/elastic/README.md` (start node → seed →
  `Search:Provider=elastic` → search) run the Elastic provider live
  against the same screen.
- **ELSER / Rerank: NOT claimed.** ELSER semantic search did **not**
  run and is not claimed — it needs an ML node / inference endpoint;
  an optional `semantic_text` mapping is provided in
  `scripts/elastic/` for a machine that has one. Elastic Rerank was
  never used and is never claimed.

## Postman — SHIPPED (collection: the sandbox flow as our app exposes it)

- `postman/solvia-paypal-flow.collection.json` — a Postman collection
  (schema v2.1) shipped in this repo, covering the flow exactly as
  Solvia's own redo API exposes it: `POST /api/paypal/orders`,
  `POST /api/paypal/capture`, `GET /verify`, and
  `POST /api/chat/extract`. Every request carries a test script
  asserting the response shape — including the capture rule
  (completed ⟺ status COMPLETED, with capture id + receipt number)
  and the honest 503 "sandbox keys not configured" shape.
- `postman/solvia-local.environment.json` — variables only, no
  secrets; `postman/README.md` — import + run steps, including how to
  create the free PayPal sandbox app whose keys the *app* (never
  Postman) reads.
- Honest boundary: the buyer-approval step between create and capture
  happens on sandbox.paypal.com in the browser flow with a sandbox
  buyer account — the collection covers the API calls around that
  human click, and says so. The redo PayPal endpoints are
  antiforgery-protected in the browser flow (see
  `docs/apimatic-context.md`); the collection describes the app's
  surface as shipped, not a bypass of it.

## APIMatic — CONTEXT DOCUMENT AUTHORED (plugin run NOT claimed)

- `docs/apimatic-context.md` — Solvia's own API surface (endpoints,
  request/response shapes, auth model, and every error state
  including the 503 not-configured) written in the context form
  APIMatic's Context Plugin consumes. It is **authored for the
  APIMatic plugin; a plugin run is NOT claimed.**
- Honest boundary: the document is authored and verified against the
  shipped redo controllers; **running the Context Plugin itself
  happens in Jose Luis's editor and has not happened** — no run is
  claimed anywhere.

## Zapier — APP SIDE SHIPPED (the live Zap is an account step, NOT done)

- Real in the app: `src/Solvia/Services/PaymentWebhookService.cs`
  POSTs one JSON event — `{permitNumber, campus, amountCents,
  paypalCaptureId, paidAtUtc, receiptIssuedAtUtc, verifyUrl}`, **no
  student PII** (no name, no email, no plate) — to the configured
  `Webhook:Url` the moment a permit goes live: a Paid capture or a
  $0 free-issue through `CheckoutService`, or a verified
  `PAYMENT.CAPTURE.COMPLETED` PayPal webhook handled by
  `PayPalWebhookHandler` — in every case only **after** the live
  transition has been saved. Empty URL (the default) = disabled. The
  send never throws and never blocks a payment: a 3-second cap,
  failures caught and logged, the already-saved permit unaffected.
  The payload and its verify-URL origin rules are covered in
  `tests/Solvia.Tests/WebhookTests.cs`. Full field list, both config
  keys, and the recipe: `docs/zapier-recipe.md`.
- **Creating the live Zap is Jose Luis's step in his own Zapier
  account; it is NOT done, and no live Zap exists or is claimed**
  until he builds it from that recipe.

## KERNEL — FEE-REFRESH JOB SHIPPED (portable script)

- `tools/fee-refresh/` — the fee-table refresh job shipped in this
  repo: `fee-refresh.sh` (bash + curl), its `sources.csv` (the 10
  public campus pages in `data/fees.csv`), and its README with a
  real sample run. The job re-walks the 10 public campus pages,
  extracts the price lines, and diffs the amounts against
  `data/fees.csv`, printing a human review report (UNCHANGED /
  CHANGED / UNREACHABLE / NO BASELINE). It **proposes changes only —
  it never edits fees.csv**.
- KERNEL's role, stated exactly: KERNEL is this job's production
  runner in the sponsor plan (cloud browser infrastructure for
  re-walking pages); the shipped artifact is the portable script
  doing the same job, with its real sample output recorded in
  `tools/fee-refresh/README.md`. No KERNEL session or replay is
  claimed.

## Render — NOT DEPLOYED (no claim)

- There is **no deployed hosted copy** of this redo. Deploying one
  needs Jose Luis's Render account. Until a deployment exists and is
  verified live, **Render is not claimed as used** — say so plainly
  wherever hosting comes up.

## QuestPDF — SHIPPED (permit PDF; Community License, NOT MIT / NOT Apache)

- The printable permit PDF is rendered at runtime by
  `src/Solvia/Services/PermitPdfService.cs` using **QuestPDF
  2026.2.4** (`PackageReference` in `src/Solvia/Solvia.csproj`).
- License, stated exactly: QuestPDF is used under its **Community
  License v3.0**, which is **free for OSI open-source projects**
  (Solvia itself is open source), with **public-sector entities
  excluded** from that free eligibility. **QuestPDF is never called
  MIT or Apache in this submission** — it is neither.
