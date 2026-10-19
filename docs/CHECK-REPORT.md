# Solvia — T10 Independent Check Report

**Checker:** T10 (independent — built nothing on this project).
**Date:** Oct 18, 2026.
**Target audited:** the PUSHED public repo — fresh clone of
https://github.com/jlhpacheco/solvia at HEAD
`7354020ab24ed46015f987eee36056701fab6858` (172 files) — plus the
local tree `~/workspace/paypal-hackathon/solvia/` for unpushed items.
**References:** BUILD-PLAN.md track T10, RULES-CHECKLIST.md,
docs/SPONSORS.md (claims source of truth), PERMIT-REFERENCE.md.

## Overall verdict: **FAIL — submission is BLOCKED**

Two checks fail (Licenses, Claims). Both are documentation fixes —
no app code is implicated. Every FAIL below names the exact file,
line, and fix. Submission stays blocked until the owning tracks fix
them and T10 re-verifies.

| # | Check | Verdict |
|---|---|---|
| 1 | Secrets | **PASS** |
| 2 | Fresh code (not copied Duvara) | **PASS** (PMB hits explained) |
| 3 | Licenses / NOTICES | **FAIL** |
| 4 | Claims audit | **FAIL** |
| 5 | Fictional data | **PASS** |
| 6 | Rules fit | **PASS** |

---

## 1. SECRETS — PASS

- No real `.env`, no `*.db`/`*.sqlite`, no `bin/`/`obj/` anywhere in
  the remote tree (172 files listed) or in any filename in the full
  git history (`git log --all --name-only` — zero hits).
- `.env.example`: every credential value empty (`PAYPAL_CLIENT_ID=`,
  `PAYPAL_CLIENT_SECRET=`); header states values are set as
  environment variables and never committed.
- `src/Solvia/appsettings.example.json`: placeholders only —
  `YOUR_PAYPAL_SANDBOX_CLIENT_ID`, `YOUR_PAYPAL_SANDBOX_CLIENT_SECRET`,
  `YOUR_ZAPIER_CATCH_HOOK_URL_OR_LEAVE_EMPTY`.
- `src/Solvia/appsettings.json` (committed): contains **no** ClientId
  or ClientSecret keys at all; `Webhook:Url` is `""` (disabled).
  `postman/solvia-local.environment.json`: variables only, empty
  values, no secrets.
- The only credential-shaped strings in the repo are obvious test
  fakes inside `tests/`: `test-client-id:test-client-secret`
  (PayPalServiceTests.cs:80), `"TEST-ACCESS-TOKEN"` (:371),
  `"stub-access-token"` (RosterVerifyTests.cs:489), literal
  `token=<redacted>` approve URLs, `ORDER-1` / `CAPTURE-1`
  (WebhookTests.cs), and the webhook target
  `https://hooks.example.test/catch/1` (WebhookTests.cs:33). No real
  hook URL, no real token, no real person's email or password exists
  anywhere in the tree — confirmed by a repo-wide email regex sweep
  (only `.example` addresses, see check 5) and a long-string sweep
  (only test names, separators, and the T9 commit hash in the log).

## 2. FRESH CODE — PASS (with explanation of the PMB hits)

- `Duvara`, `CarPermit`, `car-permit`, `Stripe`: **zero hits**
  anywhere in the repo (code, docs, tests, data) — word-boundary
  grep, vendor `wwwroot/lib` included (Bootstrap's own "striped"
  CSS classes are the only near-match and are not the word Stripe).
- Reference-only machinery is absent, not renamed: repo-wide grep
  for `Stripe|UserCategory|ReceiptPdf|RowVersion|QuestPDF|SkiaSharp|
  Npgsql|Identity` returns nothing. The app csproj references exactly
  one package (`Microsoft.EntityFrameworkCore.Sqlite` 10.0.13) — no
  Identity stack, no Postgres, no PDF/QR libraries.
- Shapes match the reference only where PERMIT-REFERENCE §10 allows:
  `PermitApplication` carries owner + vehicle + type + dates +
  `PriceCents` + `PaymentStatus` + `PayPalOrderId`/`PayPalCaptureId`
  + `PaidAtUtc` + `ReceiptNumber` + `VerifyToken`
  (src/Solvia/Models/PermitApplication.cs:13-71), the composite
  unique index guards duplicates, and Paid is set only from a
  COMPLETED capture. Solvia's own divergences prove fresh authorship:
  `StudentId` (not `UserId`), an added `Campus` field, no
  `UserCategory`, no Stripe fields, no `ReceiptPdfBytes`, no
  `RowVersion`; `PermitValidity` is re-implemented as Solvia's
  roster/verify logic (src/Solvia/Services/PermitValidity.cs:1-30),
  not the reference's static class.
- No seeded demo-admin pattern: there is no Identity, no admin user,
  no password anywhere. Seeding is EF `HasData` of the two permit
  types and the four fictional students only
  (src/Solvia/Data/SolviaDbContext.cs:118-165), created by
  `EnsureCreated()` (src/Solvia/Program.cs:154-161) — the reference's
  `DbInitializer`/startup-seed pattern was not copied.
- **PMB / mycampuspermit hits — explained, NOT a fail.** 65 grep hits
  for `PMB|mycampuspermit`, all of one kind: factual **source
  attribution** for the fee research data — the Source column of
  `data/fees.csv` (e.g. "PMB page opened — mycampuspermit.com/
  mbay.html"), README.md:24 and README.md:196 (data-source notes),
  docs/USE-CASE.md:57 (naming the incumbent store as market context),
  `tools/fee-refresh/sources.csv` (the refresh job's source URL
  list), and code comments (Services/CampusFee.cs:5,
  Services/FeeCatalogService.cs:13). RULES-CHECKLIST §4 Ownership
  *requires* the fee data to carry its source note, and GAP #8 bans
  competitor **logos/branding and copied page text/design** — none of
  which appear: no PMB logo, no copied page copy, and PMB is never
  presented as Solvia's brand or partner. The references are
  nominative and factual. (Strategy note for the coordinator, not a
  rules fail: these mentions live in data/docs, not in any video or
  submission surface.)

## 3. LICENSES — **FAIL**

What is right: `LICENSE` at the repo root is the MIT License,
"Copyright (c) 2026 Jose Luis Pacheco", and GitHub detects it —
verified live during this audit via the GitHub API
(`private: false`, `license.spdx_id: MIT`). `NOTICES.md`:10-11 names
Phi-4-mini (MIT) correctly, and :12 names
Microsoft.EntityFrameworkCore.Sqlite 10.0.13 (MIT) — matching the one
package in `src/Solvia/Solvia.csproj`.

Why it fails: **`NOTICES.md` is still the T0 stub and is incomplete
for what actually ships.** Its own text admits it —
NOTICES.md:3-5 ("Stub created by track T0… Track T10 completes this
file") and NOTICES.md:12-13 (AG Grid "license line to be finalized
by T10"). Missing entries, each a real shipped third-party piece:

1. **AG Grid Community 34.3.1** — ships (loaded from the official
   jsDelivr CDN, `src/Solvia/Views/Shared/_FeeTable.cshtml:26-30`)
   but NOTICES gives it no license (it is MIT). Fix: add the entry.
2. **Bootstrap 5.3.3** (MIT) — ships in `wwwroot/lib/bootstrap/`
   (its LICENSE file is in-tree) but is never named in NOTICES.md.
3. **jQuery 3.7.1** (MIT) — ships in `wwwroot/lib/jquery/`
   (LICENSE.txt in-tree), unnamed in NOTICES.md.
4. **jQuery Validation 1.21.0** (MIT) — ships in
   `wwwroot/lib/jquery-validation/` (LICENSE.md in-tree), unnamed.
5. **jQuery Validation Unobtrusive** (MIT, .NET Foundation) — ships
   in `wwwroot/lib/jquery-validation-unobtrusive/` (LICENSE.txt
   in-tree), unnamed.
6. **Ollama** — BUILD-PLAN T10 requires an Ollama terms line; the
   model *runner* is mentioned in NOTICES.md:10-11 only as transport,
   with no license/terms entry (external tool, not shipped code —
   say exactly that).
7. **Test-only dependencies** — xUnit 2.9.3, xunit.runner.visualstudio
   3.1.4, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4
   (`tests/Solvia.Tests/Solvia.Tests.csproj`) are unlisted; one line
   ("test-only, not shipped in the app") closes it.

Fix: complete NOTICES.md with entries 1-7, remove the stub header,
then T10 re-verifies against the two csproj files and `wwwroot/lib/`.

## 4. CLAIMS AUDIT — **FAIL**

README.md, postman/README.md, tools/fee-refresh/README.md,
docs/apimatic-context.md, and docs/zapier-recipe.md were checked
against docs/SPONSORS.md and the shipped code and are **accurate**:
README:41 (Elastic seam, "ELSER did not run and is not claimed"),
README:41 (Bryntum not used), README:44 (Render "planned, not yet
deployed… no hosted URL yet"), README:43 (APIMatic context document;
Postman collection; Zapier "receipt email + ledger sheet; the live
Zap is created in your own Zapier account"; KERNEL "the portable
script ships, KERNEL is its production runner in the plan"),
docs/apimatic-context.md:9-13 (plugin run "has not happened yet"),
tools/fee-refresh/README.md:8-16 (script + output claimed, KERNEL
named as production runner "— nothing more"). Three items fail, all
in `docs/USE-CASE.md` plus one repeated wording:

1. **FAIL — docs/USE-CASE.md:47** still reads
   `Receipt sent (Zapier step: receipt + renewal reminder).`
   No renewal-reminder step exists anywhere in the shipped Zapier
   artifact — docs/zapier-recipe.md is Catch Hook → receipt email →
   Google Sheets ledger row, and docs/SPONSORS.md:82-98 claims
   exactly that. (The same drift was already fixed in README.md and
   is self-flagged in docs/CLEAN-INSTALL-LOG.md:122-124.)
   **Fix:** line 47 → "Receipt sent (Zapier step: receipt email +
   ledger row — see docs/zapier-recipe.md)."
2. **FAIL — docs/USE-CASE.md:29** says the fee-table lookup is
   "(Elastic search)". Reality per docs/SPONSORS.md (Elastic
   section) and the code: the default and demo provider is the
   built-in deterministic search (`Search:Provider=builtin` in
   appsettings.json); the Elastic provider is a config seam that
   **never ran live**, and SPONSORS.md states no "powered by
   Elasticsearch" claim is made. USE-CASE.md makes exactly that
   claim. **Fix:** line 28-29 → "(built-in college search — an
   Elastic provider sits behind a config seam; see
   docs/SPONSORS.md)".
3. **Minor, fix with the same edit — "function calling" wording.**
   README.md:38 ("function calling for extracting school / car /
   term") and docs/USE-CASE.md:23 ("extracts her answers as function
   arguments") describe a mechanism the code does not use:
   `src/Solvia/Services/OllamaExtractionService.cs` calls Ollama
   `/api/chat` with `format = "json"` (line 113) and validates the
   parsed JSON against the catalog — there is no function/tool-
   calling invocation. **Fix:** both places → "structured JSON
   extraction".

Sub-checks inside this audit that **PASS**, stated explicitly:

- **No text anywhere claims California or the Chancellor's Office
  banned China AI.** docs/PRIVACY.md:35-39 (Honesty box) says the
  opposite verbatim: "Neither the California Community Colleges
  Chancellor's Office nor the State of California bans China AI
  models… We never claim that it does." PRIVACY.md §1 frames the
  guidance as FERPA / do-not-share / do-not-allow-training, and §3
  labels the country rule "Our country rule… our own choice"
  (reinforced in the coordinator's record; the doc's framing is
  guidance = data protection, exclusion = ours).
- **Country-rule wording is exact everywhere it appears:**
  README.md:49, docs/USE-CASE.md:53, docs/PRIVACY.md:31 — "no China
  tools, models, or services, and no Russia tools, models, or
  services — anywhere in the stack; only those two are out." A
  repo-wide grep for `USA only|U.S. only|US-only` returns zero hits;
  "U.S. (Microsoft) open model" describes the model only, never the
  whole stack.
- **Bryntum:** every mention in the repo (README:41, SPONSORS.md,
  code comments in Services/ITermDatesService.cs:8-10,
  wwwroot/js/calendar.js:3-7) states it is NOT embedded/used. No
  shipped claim exists.

## 5. FICTIONAL DATA — PASS

- Seeded students are exactly four fictional people —
  maria/diego/priya/sam (src/Solvia/Data/SolviaDbContext.cs:140-164)
  — and a repo-wide email sweep returns **only**
  `maria@student.example`, `diego@student.example`,
  `priya@student.example`, `sam@student.example`. No real email,
  name, or plate belongs to a real person anywhere in code, seeds,
  tests, or docs. (The only real name in the repo is the entrant's
  own, as LICENSE copyright holder and author — required, not demo
  data.)
- Plate strings in tests are pattern-fictional (e.g. `8XYZ123`,
  ExtractionTests) — the classic placeholder pattern, not a real
  plate.
- The repo contains no screenshots of real people or accounts; the
  only image asset is the project's own logo (check 6).

## 6. RULES FIT — PASS

- **Public repo + detected license:** verified live during this
  audit via the GitHub API — `private: false`, visibility public,
  `license.spdx_id = MIT` (RULES-CHECKLIST GAP #5 closed).
- **README run steps:** present and complete (README.md:70-190:
  .NET 10 SDK → clone → optional sandbox keys with developer.paypal
  .com steps → optional Ollama/`phi4-mini` → build/run → the $0 free
  walk), and independently executed from a fresh clone in
  docs/CLEAN-INSTALL-LOG.md (T9: build 0/0, all pages 200, Priya's
  free path to a VALID /verify with keys and Ollama skipped).
- **Copyrighted assets:** the only image, `assets/logo.webp`, is the
  project's own generated logo (README.md:1). No college logos, no
  PayPal logos, no third-party artwork ship in the repo.
- **Trademark use:** PayPal is named nominatively only — as the
  payment provider the app integrates (README tools table, "Why
  PayPal" section) — never as Solvia's own branding; the project
  name and logo are original (PMB naming is data attribution, see
  check 2).
- **Timing:** all 7 commits are dated 2026-10-18 — build activity
  inside the Oct 1 – Nov 12, 2026 submission window, and the project
  is new (README.md:66 states it; git history confirms).
- **English only:** all submission materials in the repo are
  English (spot sweep for Spanish content words: zero hits).
- **Maria's demo college has a published price:** Fresno City
  College, $30 Fall/Spring — `data/fees.csv:19` — one of the 10
  campuses with a published semester price. `data/fees.csv` itself
  is byte-identical to the verified research file (SHA-256
  `6df00f9dea4de01462284fe6e11472cf1b6f0a292a114812a8c3847d3b4c96f2`,
  header + 25 rows).

## Local-tree observations (not verdicts)

- The local tree's tracked content is **identical** to the clone
  (`diff -rq` clean). Local-only extras are build output (`bin/`,
  `obj/` — covered by `.gitignore`, correctly unpushed) and four
  unpushed logo-generation intermediates in `assets/`
  (`media-generation-solvia-logo-*.webp` + two `*.json` sidecars) —
  working files, not secrets; the chosen `assets/logo.webp` is the
  pushed one. Nothing unpushed *should* be in the repo, and nothing
  in the repo is missing locally.

## What unblocks submission

1. Complete `NOTICES.md` per Check 3, items 1-7 (docs track / T10
   file per BUILD-PLAN).
2. Fix `docs/USE-CASE.md:47` and `docs/USE-CASE.md:29` per Check 4,
   items 1-2, and the two "function calling" wordings (Check 4,
   item 3).
3. Push, then T10 re-verifies the three files on the new HEAD and
   flips this report's verdict. (Separate standing items outside
   this audit's scope remain as recorded elsewhere: demo video,
   Render hosted copy if claimed, final-submit read-back.)

---

## T10 RE-VERIFICATION — 2026-10-18 (independent checker, fix commit e871a559)

Method: fresh `git clone` of https://github.com/jlhpacheco/solvia at HEAD
`e871a559cac0abd801a34f3635090a3925f318b6`; only the previously failed
items were re-checked.

1. **NOTICES.md (Check 3) — PASS.** No stub/"to be finalized"/TODO text
   remains. All required entries present with license names: Phi-4-mini
   (MIT, Microsoft), Ollama (MIT; external tool, NOT bundled, optional),
   EF Core Sqlite 10.0.13 (MIT), Bootstrap 5.3.3 (MIT), jQuery 3.7.1
   (MIT), jQuery Validation 1.21.0 (MIT), jQuery Validation Unobtrusive
   (MIT), AG Grid Community 34.3.1 (MIT, CDN-loaded), and test-only deps
   (xUnit 2.9.3 + runner — Apache License 2.0; Microsoft.NET.Test.Sdk
   17.14.1 — MIT; coverlet.collector 6.0.4 — MIT). Spot-checks:
   Bootstrap's in-tree LICENSE (`wwwroot/lib/bootstrap/LICENSE`) reads
   "The MIT License (MIT)" exactly as NOTICES states; the code pins
   `ag-grid-community@34.3.1` on the jsDelivr CDN
   (`src/Solvia/Views/Shared/_FeeTable.cshtml:26-30`) exactly as NOTICES
   states; jQuery's in-tree LICENSE.txt (OpenJS Foundation) is the file
   NOTICES cites for its MIT entry.
2. **USE-CASE.md claims (Check 4, items 1-2) — PASS.** "renewal reminder"
   is gone; line 48 now reads "Receipt sent (Zapier step: receipt email
   + ledger row — see docs/zapier-recipe.md)". "(Elastic search)" is
   gone; line 29 now reads "(built-in college search — an Elastic
   provider sits behind a config seam; see docs/SPONSORS.md)".
3. **"Function calling" wording (Check 4, item 3) — PASS.** Zero hits in
   README.md and docs/USE-CASE.md; USE-CASE.md:23 reads "extracts her
   answers as structured JSON" and README.md:38 reads "structured JSON
   extraction of school / car / term".
4. **Fix scope sanity — PASS.** `git diff --name-only 7354020a e871a559`
   = NOTICES.md, README.md, docs/CHECK-REPORT.md, docs/USE-CASE.md
   only. No app code changed.

**OVERALL RE-VERDICT: PASS.** Both original FAIL checks (3 Licenses,
4 Claims) are cured on HEAD e871a559. The submission block from the
original T10 audit is lifted. (Standing items outside this audit's
scope remain as recorded: demo video, Render hosted copy if claimed,
final-submit read-back — all gated on Jose Luis's word.)

---

## REDO re-verification — Oct 18, 2026

The T10 report above stands as the v1 record, unedited. Below is the REDO edition of this report in full: the independent 60-item sign-off against the rebuilt tree (51 PASS, 9 OUT by plan, 0 FAIL), including the dated RE-CHECK addendum that closed Finding 1 and opened the §4 swap gate. The swap commit this file ships in is the REDO swap — one commit parented on the v1 freeze `595ea23e`.

# CHECK-REPORT — REDO edition (the 60-item sign-off)

**Checker:** R10 — independent; built nothing in this tree.
**Date:** Oct 18, 2026 (evening, PDT)
**Tree:** `solvia-redo/` as delivered by R0–R9.
**Method:** every line below is evidence the checker personally
saw — code/view reads, tree greps, a full suite run executed by
the checker on the final tree, and a live walk on a pristine copy
(see CLEAN-INSTALL-LOG.md). Dispositions are REDO-PLAN §1's; an
item is signed against the plan's adaptation, not against
Duvara's literal text where the plan overrode it.

## Gate results (before the items)

| Gate | Result |
|---|---|
| Build, final tree (`dotnet build src/Solvia.slnx --no-incremental`) | **0 warnings / 0 errors** |
| Full suite, checker's own run (copied-folder xUnit console runner, Lyceum recipe) | **180 total, 180 passed, 0 failed** |
| `data/fees.csv` SHA-256 | `327aca14c33f99a1ffd412ccdbb8ec434b02c1cb440c17d2e5ce5edfabe5771c` — **matches** the required hash |
| Secret sweep (whole tree, config + code + docs patterns) | **Clean** — placeholders only (`YOUR_PAYPAL_SANDBOX_CLIENT_ID`, …); no real credential-shaped value anywhere |
| Frozen trees needed at runtime? | **No** — the pristine copy ran standalone (own `data/fees.csv`, own assets); externals are NuGet restore + the AG Grid CDN only |
| Clean install from README alone | **FAIL — see Finding 1. The swap is BLOCKED.** |
| README.md / NOTICES.md (R10-owned, finished this pass) | README wording is honest and matches the tree; NOTICES complete (QuestPDF = Community License v3.0, NOT MIT/Apache; Phi-4-mini MIT; AG Grid Community; EF Core Sqlite MIT) |
| Swap executed? | **No — correctly withheld.** The gate failed, so per REDO-PLAN §4 there is nothing to swap yet. |

### Finding 1 — FAIL (swap-blocking): the app does not boot when run exactly as the README directs

From a pristine copy, `dotnet run --project src/Solvia` (README
Step 5, run from the repo root) crashes before listening:

```
Unhandled exception. System.IO.FileNotFoundException: College fee data file not found: 'data/fees.csv'. Set FEES_CSV_PATH (or FeesCsvPath) to the repo's data/fees.csv.
   at Solvia.Services.FeeCatalogService..ctor(String csvPath) in src/Solvia/Services/FeeCatalogService.cs:line 40
   at Program.<>c.<<Main>$>b__0_1(IServiceProvider sp) in src/Solvia/Program.cs:line 82
```

Cause, from the code: `dotnet run --project` executes with the
project folder as working directory (the run's `solvia.db` landed
in `src/Solvia/`). The redo's Program.cs passes the relative
configured `FeesCsvPath` (`data/fees.csv`) un-anchored to
FeeCatalogService; the frozen v1 tree anchored it to
`ContentRootPath/../../` and the redo dropped the anchor.
**Owned by R0 (Program.cs) / R1 (FeeCatalogService).** The
checker fixed nothing. With the documented `FEES_CSV_PATH` knob
set, the same pristine copy boots and the entire free path walks
green (CLEAN-INSTALL-LOG.md) — the defect is this one path, not
the application. Re-verification required after the fix: pristine
copy, README steps only, Step 5b green.

### Caution for the swap manifest (not a checklist item)

The redo tree contains **no LICENSE file**. The swap tree is
built from the redo manifest alone (REDO-PLAN §4), so unless the
manifest adds the repo's existing MIT LICENSE, the swapped repo
loses its top-level license — which the hackathon requires to be
detectable and which README.md links. Add LICENSE to the swap
manifest (or have R0 place it in the redo tree) before the swap.

## The 60 items

**A. Apply form**
1. PASS — BuildApplyModelAsync loads the caller's own vehicles + Active types only, types ordered by Name [read]; suite [180].
2. PASS — Option label "Plate · State · Make Model" [read]; POST re-loads the vehicle owned-by check, else the general invalid-selection error [read]; label seen live: "9PRIYA1 · CA · Honda Civic" [live].
3. PASS — Active types only, label = Name [read]; both options seen in the live form [live].
4. PASS — Start Date `<input type="date">`, GET defaults it to today [read]; default value present in the served form [live]; no end-date input exists in the view [read].
5. PASS — One antiforgery "Submit Application" POST; every failure re-renders the SAME form, lists repopulated, errors on-screen [read]; FlowTests [suite].
6. PASS — Zero vehicles → "Add a vehicle first" card + button, form replaced [read]; seen live as Priya [live].
7. PASS — Zero Active types → "No permit types available" card, types said to live "in the database" [read].
8. PASS — "What happens next" 3-step card + trust note on the Apply page [read].

**B. PermitType model**
9. PASS — Row is exactly Id, Code (EF text conversion), Name (required, ≤200), DurationDays, PriceCents, Active [read].
10. PASS (plan ADAPT) — Exactly two types seeded; PriceCents seeded 0 as a non-authoritative placeholder; startup validation (`ValidateStartupData`) throws naming the failed check (catalog/active/duration/term-price/day-pass-price) [read]; throws pinned by DomainTests [suite]; guard test prices Fresno Term at 3000 from the catalog while the type row is 0 [suite].
11. PASS (plan ADAPT) — Unresolved price re-reads the catalog row and blocks naming the campus and the unpublished price; no write-back to the PermitType row exists in any service [read].

**C. Apply POST rules**
12. PASS — UserCategoryResolver: override flag > employee domain > student domain > Public [read]; Faculty/Visitor exist in the enum and price as Public [read]; Elena (Employee) / Pat (Public) seeded [read]; category snapshotted onto the row [live: Priya's row carries Student].
13. PASS (plan ADAPT) — Daily → end = start [read]; Term → the term window's PermitEndDate from TermDatesService [read]; Priya's Term row ends 2026-12-22 from the calendar [live].
14. PASS — CreateAsync writes owner, vehicle, type, start/end, Category snapshot, PriceCents snapshot, PaymentStatus NotRequired-if-0 else Pending, Status Pending, SubmittedAtUtc [read]; the live DB row matches field for field [live].
15. PASS — Composite unique index (StudentId, VehicleId, PermitTypeId, StartDate, EndDate) [read]; pre-check + DbUpdateException catch → "already submitted…" on the same form [read]; ApplicationTests [suite].
16. PASS — "Please select a vehicle." / "Please select a permit type." / general invalid-selection / error naming the campus fee [read].
17. PASS — $0 → NotRequired + issuance inside CreateAsync → redirect My Permits; Checkout never rendered [read]; Priya: POST → 302 /Permits/My with a permit number already minted [live].
18. PASS — Paid outcome redirects to /Checkout/{id} [read]; FlowTests' Maria/Fresno walk [suite].

**D. Checkout page**
19. PASS — Unknown or not-owner → 404; Paid or PriceCents ≤ 0 → redirect My Permits; page never renders in those cases [read]; CheckoutTests + R6 live walks (unknown 404, other person's 404, Paid → redirect) [suite].
20. PASS — Summary tiles + detail list incl. type, plate + state, valid range, amount at 2dp with currency from `PayPal:Currency` (default USD), campus and category [read: controller + view; wiring-pass live walk saw tiles and the $30.00 form].
21. PASS — Explainer copy (students/public pay the published fee; employees exempt and never see this page) + trust box "Verify permit, vehicle, and amount before paying" [read via R6 view; seen in the wiring-pass live walk].
22. OUT — By plan: Stripe is not Solvia's path; PayPal Orders is the live and only processor.

**E. Stripe flow**
23–29. OUT — By plan: no Stripe SDK, pages, webhook, or embedded view is built (package list and controller list contain no Stripe [read]).

**F. PayPal machinery**
30. PASS — POST /api/paypal/orders, antiforgery, owner-checked (404 unknown / 403 wrong owner) [read]; idempotent — a Pending app's stored order id returns, never a second order [read; CheckoutTests prove the fake handler saw exactly one create]; Employee → NotRequired + issued, "not-required" [read/suite]; already Paid → "already-paid", no order [read]; payable ≤ $0 → hard CheckoutPricingException [read]; missing config → honest 503 [read].
31. PASS — Capture by order id, owner-checked first [read]; only a COMPLETED capture sets Paid + capture id + PaidAtUtc (ApplyCaptureAsync) [read]; buyer return alone changes nothing [read]; CheckoutTests [suite].
32. PASS — PayPalService is the only PayPal talker: OAuth token, create, capture, VerifyWebhookSignatureAsync (webhook id from `PayPal:WebhookId`) [read]; it is the only typed client registered over the PayPal base URL [read: Program.cs].
33. PASS — Webhook: antiforgery-exempt controller, signature-verified via item 32; empty body / failed verify / unconfigured → 401, missing event id → 400 [read]; idempotent via WebhookEventLog unique (Provider, EventId) — redelivery is a logged no-op [read]; only PAYMENT.CAPTURE.COMPLETED acts, finds the application BY CAPTURE ID, amount ≠ snapshot or Employee/$0 → Failed with no issuance, else Paid + PaidAtUtc if unset + issuance [read]; other types logged Ignored [read]; WebhookTests [suite].

**G. Issuance**
34. PASS (plan ADAPT — format-equivalence accepted per plan) — One mint path (`IssuanceService.IssueIfAbsentAsync`, mint-if-absent) with exactly three call sites: free-at-Apply, COMPLETED capture, verified webhook [read]; SOL-YYYY-NNNNNN + verify token + ReceiptIssuedAtUtc stamped at mint [read]; uniqueness retried ≤10× (MaxAttempts = 10) against unique indexes [read].
35. PASS (plan ADAPT) — No SMTP built; the outbound payment-complete webhook is the receipt-out mechanism, fires only after the live truth is saved, never blocks a payment; payload carries permitNumber, verifyUrl, receiptIssuedAtUtc, campus, amount — and no student name/email/plate [read]; payload greps in WebhookTests [suite].

**H. My Permits**
36. PASS — Own applications only, newest-first by SubmittedAtUtc [read]; header carries the "Apply for a Permit" button [read]; bottom "Get another permit" link [read].
37. PASS — Tiles: total / Paid / pending-payment (price > 0 and not Paid), own rows only [read]; all three tiles present in the live page [live].
38. PASS — Duvara column set incl. Category, Submitted in Pacific time, "Not Required" spelled out, amount in parentheses when > 0; mobile cards with badges [read]; MyPermitsTests [suite]; live row showed the number + "Not Required" [live].
39. PASS — "Pay Now" only when price > 0 and not Paid → /Checkout/{id} with target="_blank" [read]; MyPermitsTests [suite].
40. PASS — "Permit (PDF)" only on live (Paid/NotRequired) rows [read]; Priya's live row carried it and it served [live].
41. PASS — Empty state "No applications yet" + Apply link [read].
42. PASS — No search/filter/sort/pagination: none in the controller or view [read]; the served page contains no such markup [grep of the live page: 0 hits].

**I. Receipt PDF**
43. PASS (plan ADAPT — no roles in the demo) — Gates: non-owner 403, unknown 404, owned-unpaid 400 with a plain-language body; download named from the receipt number (Guid fallback) [read]; all four outcomes proven live by R5's walk and the 400 re-proven under real DI by the wiring pass; PermitPdfTests [suite].
44. PASS — QuestPDF, Letter: title/org/rule, summary box (permit # or "(pending)", issued stamp, EXEMPT (Employee) / FREE — ISSUED naming the campus / PAID + amount payment lines, privilege line), details table incl. Category + PayPal order/capture ids, "Proof of Parking (For Enforcement)" section with /verify?t= URL + token block, Pacific footer [read + greps]; Elena's EXEMPT text read back from a generated PDF by R5; Priya's PDF served 200 %PDF [live].
45. PASS — Regeneration on every call; the application carries NO PDF cache fields (full field list read) [read]; two consecutive live calls both 200 (R5 walk).

**J. Public verification + validity**
46. PASS (plan ADAPT) — /verify?t= anonymous; page shows type, plate STATE only, category, privilege line, payment status word, last-updated, "(not issued)" defensive state; unknown/missing token → neutral page, nothing leaked [read]; lightweight in-memory limiter 60/min/IP wired on both verify doors, 429 on trip [read]; anonymous VALID with the full field set [live]; VerifyTests incl. burst [suite].
47. PASS — PermitValidity.Check is exactly: start ≤ day ≤ end AND Status ≠ Denied AND PaymentStatus ∈ {Paid, NotRequired} [read].
48. PASS — GET /api/verify?t= JSON twin: same token, same PermitValidity verdict, same field set, nothing extra [read]; live JSON for Priya's token matched the page's verdict and fields, exposing plateState but never the plate [live].

**K. Statuses + lifecycle**
49. PASS — PaymentStatus = NotRequired, Pending, Paid, Failed, Refunded, Expired, PartiallyRefunded [read]; exercised transitions are the plan's (capture/webhook/settle) [read].
50. PASS — ApplicationStatus = Pending, Approved, Denied; created Pending; no admin approve/deny action exists [read].
51. PASS — Guid ConcurrencyToken with IsConcurrencyToken(); every saving service rotates it before save (ApplicationService, CheckoutService, webhook handler, PriceRepairService) [read + greps]; a stale write surfaces as a same-screen error on Confirm [read]; token-change pinned [suite].

**L. Admin / back-office orbit**
52. PASS — No permit-type management UI exists or was built; types live in seed only (no controller/view manages them) [read]; the empty-types card's "in the database" wording stands [read].
53. PASS (plan ADAPT) — Roster header tiles per campus: total issued (Paid + NotRequired), revenue = sum of Paid PriceCents snapshots, expired count (EndDate < today and issued) [read]; RosterVerifyTests against a flow-produced scenario [suite].
54. PASS (plan ADAPT) — Roster "By student" section: one row per seeded student, permit count at this campus, issued receipt numbers, sorted by permit count descending — and nothing more [read]; RosterVerifyTests [suite].
55. PASS (plan ADAPT) — PriceRepairService.RepairPendingAsync recomputes Pending rows only, from the fee catalog; Paid/NotRequired snapshots never rewritten; unpublished fee leaves the row untouched and reports skipped; POST /College/RepairPrices shows the change list as a roster banner [read]; PriceRepairTests [suite].

56. OUT — By plan: citations/enforcement are a different module; /verify is the enforcement-facing surface.

**M. Explicitly absent in Duvara**
57. PASS — The form door references the vehicle by id only; no vehicle fields on Apply [read: Apply.cshtml]; vehicle CRUD stays in the garage.
58. PASS — No zones/lots/waitlist/renewal/transfer/cancellation/refund-action for applications [grep of the route/controller surface: none].
59. PASS (plan ADAPT) — Apply POST outcomes are redirects + status banners only; no application confirmation page exists [read]. The post-payment Receipt/Done pages are payment receipts (items 44/20 surfaces), as the plan rules.
60. PASS (plan ADAPT) — Return/Cancel are capture-or-release plumbing that redirect with a banner and render no page [read]; Return with a bogus token → home + error banner, no success state, no row Paid [R6 live walk; CheckoutTests in the suite].

## Tally

- **PASS: 51** (items 1–21, 30–55, 57–60)
- **FAIL: 0 of the 60 items**
- **OUT (by plan): 9** (items 22, 23, 24, 25, 26, 27, 28, 29, 56)
- **Gate: FAIL** — Finding 1 (clean install as documented crashes
  on the un-anchored `data/fees.csv` path). Under REDO-PLAN §4
  step 2, one gate failure = **no swap**. The swap was NOT
  executed. When R0/R1 anchor the path, the re-check is small:
  pristine copy → README steps only → app boots → Priya path →
  this report's Finding 1 closes, and the swap may proceed (with
  the LICENSE caution above folded into the manifest).

---

## RE-CHECK — Finding 1 (Oct 18, 2026, late evening PDT)

**Re-checker:** independent fresh eyes — built nothing in this
tree, fixed nothing, changed no app or test code. Only this
addendum and the CLEAN-INSTALL-LOG re-check entry were written.

**Verdict: Finding 1 is CLOSED. The §4 gate is OPEN.**

Evidence, all gathered by the re-checker's own hands:

- **Pristine copy.** `solvia-redo/` copied source-only (no
  `bin/`, no `obj/`, no `*.db`; 206 files) to
  `~/workspace/paypal-hackathon/recheck-scratch/` — on the home
  filesystem, not `/tmp` (the Lyceum tmpfs trap). The copy was
  deleted after the run.
- **Environment.** `FEES_CSV_PATH`, `FeesCsvPath`,
  `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PayPal__ClientId`,
  `PayPal__ClientSecret`, `PayPal__WebhookId`,
  `ConnectionStrings__SolviaDb`, and `ASPNETCORE_URLS` were all
  explicitly UNSET for the build and the run. No PayPal keys; no
  Ollama.
- **Build (README Step 5).** `dotnet build src/Solvia.slnx` from
  the copy's root → **0 warnings / 0 errors**.
- **Boot (README Step 5, the exact step that failed).**
  `dotnet run --project src/Solvia` from the copy's root →
  `Now listening on: http://localhost:5156`; the boot log
  contains **no FileNotFoundException**; `GET /` → **200**. The
  un-anchored `data/fees.csv` crash does not reproduce.
- **Priya's free path on that pristine instance** (forms replayed
  from their own hidden fields; see CLEAN-INSTALL-LOG.md
  re-check entry): switch to Priya → "Add a vehicle first" card →
  vehicle added (9RECHK1, Civic via "Other — type it") → Apply at
  Modesto Junior College / Columbia College / YCCD, Term → lands
  on `/Permits/My` with **SOL-2026-885833** issued and
  "Not Required" shown → Receipt page check link → anonymous
  `/verify?t=…` (no cookies) answers **VALID**.
- **LICENSE.** Present at the redo root. SHA-256
  `2c73a01d6dfb115c5914c9d69611c7af18d2a6f73b575c7fb27fb53350182874`
  — equals the frozen tree's LICENSE exactly. The swap-manifest
  caution in the original report is resolved: the manifest can
  carry this file from the redo tree itself.
- **`data/fees.csv` SHA-256.**
  `327aca14c33f99a1ffd412ccdbb8ec434b02c1cb440c17d2e5ce5edfabe5771c`
  — matches the required hash (checked on the final tree before
  and after all runs, and inside the pristine copy).
- **Full suite, re-checker's own run** on the final tree's build
  output (copied-folder xUnit console runner, Lyceum recipe):
  **Total: 180, Errors: 0, Failed: 0, Skipped: 0** — the same
  count R10 recorded. Harness note, for the next checker: the
  tests' `FindFeesCsv()` walks ancestors of the test assembly
  looking for `data/fees.csv`, so the copied test output must sit
  under a folder that contains `data/` — the re-checker's first
  placement outside such a folder failed 131 tests on that
  lookup alone; re-placed under the pristine copy (byte-identical
  tree), the suite ran 180/180. No code was changed either time.

The 60-item sign-off above stands unchanged (51 PASS, 9 OUT by
plan, 0 FAIL). With Finding 1 closed and the LICENSE caution
resolved, nothing in this report blocks the §4 swap.
