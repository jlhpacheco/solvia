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
