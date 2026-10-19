# Solvia — sponsor pieces: what actually shipped

Honest record, per the sponsor plan: a sponsor is claimed only where a
real piece of it runs in the app.

## AG Grid — SHIPPED (fee table)

- The college-side fee table (`/College/Fees`, rendered through
  `Views/Shared/_FeeTable.cshtml`) runs **AG Grid Community edition**,
  v34.3.1, loaded from its official CDN (`cdn.jsdelivr.net/npm/ag-grid-community@34.3.1`).
- Community is the **free edition**: no license key, no trial. Solvia
  deliberately never uses an AG Grid trial license — a 45-day trial
  started at build time would expire before judging ends (Dec 15, 2026).
- The only AG Grid integration point in the codebase is
  `wwwroot/js/feetable.aggrid.js`. All 25 campuses render through it,
  sortable and filterable, with prices taken exactly from
  `IFeeCatalog` (null = "Not published", FREE = "$0 FREE").
- Fallback behind the same seam: set config `FeeTable:Renderer=native`
  and the identical rows render as a plain server-rendered table — no
  other code change. If the CDN is unreachable in AG Grid mode, the
  adapter also falls back to the native renderer automatically.

## Bryntum Calendar — NOT SHIPPED (decided in T5, honestly)

- **What ships instead:** the term dates render through Solvia's
  native calendar list (`wwwroot/js/calendar.js`) on `/College/Dates`,
  fed by the exact same `TermDates` JSON (campus, term, term
  start/end, permit end = term start + DurationDays, renewal date)
  that is also served at `/College/DatesJson`. No Bryntum code, trial
  or otherwise, is embedded anywhere in this app.
- **Why — the exact reason:** unlike AG Grid, Bryntum has **no free
  community edition**. Per Bryntum's own site (checked Oct 18, 2026),
  Bryntum Calendar is available only as a paid license (from
  $600/developer) or a **free 45-day trial**. A trial started at build
  time (mid-October 2026) would expire in early December — **in the
  middle of judging (Dec 1–15, 2026)** — leaving judges a dead or
  watermarked calendar. A calendar that dies mid-judging is worse
  than a clean native one, so the trial was never started and no
  Bryntum claim is made anywhere in the submission.
- **The drop-in seam (if a licensed Bryntum build is ever used):**
  1. Add `wwwroot/js/calendar.bryntum.js` consuming the `TermDates`
     JSON island (`#term-dates-data` on `/College/Dates`, same shape
     as `/College/DatesJson`).
  2. Set config `Calendar:Renderer` to `bryntum` (bound by
     `CalendarOptions` in `Config/AppOptions.cs`, wired in
     `Program.cs`; `CollegeController.Dates` passes the flag to the
     view).
  3. `Views/College/Dates.cshtml` already switches the script include
     on that flag — no controller or data changes are needed.
  Until a real license exists, the flag stays `native` and the seam
  stays unused.

## Postman — SHIPPED (workflow: the sandbox flow as our app exposes it)

- `postman/solvia-paypal-flow.collection.json` — a Postman collection
  (schema v2.1) covering the flow exactly as Solvia's own API exposes
  it: `POST /api/paypal/create-order`, `POST /api/paypal/capture`,
  `GET /verify`, `POST /api/chat/extract`. Every request carries a
  test script asserting the response shape — including the capture
  rule (completed ⟺ status COMPLETED, with capture id + receipt
  number) and the honest 503 "sandbox keys not configured" shape.
- `postman/solvia-local.environment.json` — variables only, no
  secrets; `postman/README.md` — import + run steps, including how to
  create the free PayPal sandbox app whose keys the *app* (never
  Postman) reads.
- Honest boundary: the buyer-approval step between create and capture
  happens on sandbox.paypal.com in the browser flow with a sandbox
  buyer account — the collection covers the API calls around that
  human click, and says so.

## APIMatic — CONTEXT DOCUMENT AUTHORED (plugin run has NOT happened)

- `docs/apimatic-context.md` — Solvia's own API surface (endpoints,
  request/response shapes, auth = none/demo, and every error state
  including the 503 not-configured) written in the context form
  APIMatic's Context Plugin consumes.
- Honest boundary: the document is authored and verified against the
  shipped controllers; **running the Context Plugin itself happens
  in Jose Luis's editor and has not happened yet** — no run is
  claimed anywhere.

## Zapier — APP SIDE SHIPPED (the live Zap is an account step, not claimed)

- Real and tested in the app: `Services/PaymentWebhookService.cs`
  POSTs one JSON event — `{permitNumber, campus, amountCents,
  paypalCaptureId, paidAtUtc}`, no student PII — to the configured
  `Webhook:Url` (or `WEBHOOK_URL`) the moment `CheckoutService`
  marks a permit Paid or confirms a $0 FREE permit. Empty URL (the
  default) = disabled. The send is fire-and-forget with logging
  under a 3-second cap: failures are proven by
  `tests/Solvia.Tests/WebhookTests.cs` to never block or roll back
  a payment (6 tests: fires on Paid, fires on free-issue, silent on
  failed capture, silent when unconfigured, survives endpoint
  outage and HTTP 500).
- `docs/zapier-recipe.md` — the exact Zap (Catch Hook → Gmail
  receipt to the student → Google Sheets ledger row) with the full
  field mapping. **Creating that Zap is Jose Luis's step in his own
  Zapier account; no live Zap exists or is claimed** until he
  builds it.

## KERNEL — FEE-REFRESH JOB SHIPPED (script + real run output)

- `tools/fee-refresh/` — the fee-table refresh job: `fee-refresh.sh`
  (bash + curl) re-walks the 25 public campus pages listed in
  `sources.csv`, extracts the price lines, and diffs the amounts
  against `data/fees.csv`, printing a human review report
  (UNCHANGED / CHANGED / UNREACHABLE / NO BASELINE). It **proposes
  changes only — it never edits fees.csv** (the file's SHA-256 was
  identical before and after the sample run).
- KERNEL's role, stated exactly: KERNEL is this job's production
  runner in the sponsor plan (cloud browser infrastructure for
  re-walking pages); the shipped artifact is the portable script
  doing the same job, with its real sample output (3 campuses
  checked Oct 19, 2026 — all UNCHANGED, incl. CSUMB's $229.50)
  recorded in `tools/fee-refresh/README.md`. No KERNEL session or
  replay is claimed.

## Render — NOT SHIPPED YET (no claim)

- There is **no `render.yaml` and no deployed hosted copy** in this
  repo as of Oct 18, 2026. The hosted judges' copy remains planned
  (the README says "planned", which is the true state) and deploying
  it needs Jose Luis's Render account. Until the file and the
  deployment exist, Render is not claimed as used.

## Elastic — SEAM SHIPPED; LIVE NODE DID NOT RUN IN THE BUILD SANDBOX (T8, honestly)

- **What shipped:** college search sits behind one contract,
  `ICollegeSearch` (`src/Solvia/Services/`). The built-in
  deterministic match (`BuiltinCollegeSearch`) is the default and
  always works. `ElasticCollegeSearch` implements the same contract
  against a local Elasticsearch `colleges` index (hybrid keyword
  `multi_match` over `campus^3` + `searchText`), selected only by
  config `Search:Provider=elastic` (`Elastic:Url` default
  `http://localhost:9200`). The pick-your-college screen
  (`/Permits/Apply?q=…`) runs through the seam and **prints which
  provider served the results**; on any Elastic failure it falls
  back to the built-in match and the screen says so. Index mapping,
  an optional ELSER mapping, a seed script that loads all 25
  campuses from `data/fees.csv`, and run instructions live in
  `scripts/elastic/`. Ten unit tests cover both providers,
  including Elastic hits through a fake handler and three fallback
  modes (node down, 503, garbage reply) — all green.
- **Verified live over HTTP in the build sandbox:** with the
  default config, searching "Fresno" returned Fresno City College
  first, labeled "served by the built-in search". With
  `Search:Provider=elastic` and no node running, the same search
  returned the same first result via the fallback, still honestly
  labeled "the built-in search" — never "Elastic".
- **What did NOT run, stated plainly:** no live Elasticsearch node
  ran during the build, so the submitted demo's search is the
  built-in provider, and no "powered by Elasticsearch" claim is
  made. Evidence (Oct 18, 2026): the build sandbox has no Docker;
  the official Elasticsearch 8.18.1 tarball is 647,847,324 bytes
  and the sandbox link measured ~0.9 MB/s (a 30 MiB probe took
  ~35 s), so the download alone (~12 min) exceeded the stretch
  track's attempt budget; the box offers 2 cores and ~2.7 GB free
  RAM; and the sandbox intercepts app-process TCP to localhost
  (proven in T3 with Ollama), so the app could not have reached a
  sandbox-local node end-to-end in any case. On a normal machine
  the four steps in `scripts/elastic/README.md` (start node →
  seed → `Search__Provider=elastic` → search) run the Elastic
  provider live against the same screen.
- **ELSER / Rerank:** ELSER semantic search did **not** run and is
  not claimed — it needs an ML node / inference endpoint (Elastic
  recommends ~8 GB with ELSER); an optional `semantic_text` mapping
  is provided in `scripts/elastic/` for a machine that has one.
  Elastic Rerank was never used and is never claimed (technical
  preview; trial/subscription + ML node required).
