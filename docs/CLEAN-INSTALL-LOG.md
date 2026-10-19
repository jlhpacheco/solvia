# Solvia — clean-install log (T9)

**Date:** October 19, 2026
**Tester:** T9 builder agent (build sandbox) — a machine that had
never run this project from this repo.
**Rule of the test:** clone the public repo into a brand-new empty
folder and follow **only the README**, step by step. Any step where
the README itself was wrong, missing, or confusing counts as a
failure and gets fixed in the README, re-pushed, and re-run.

**Code under test:** public repo `jlhpacheco/solvia`, commit
`4f21770e6dad05b3ff2504770d3ababe6b1addab` (the T9 full-tree push:
171 files, read back byte-exact — every remote blob SHA matched the
locally computed git blob SHA). Repo state at clone time, checked
independently of the push: `private: false`, visibility public,
GitHub license detection = MIT.

## What the README was fixed to say — before the test

The pre-test sweep of the README against `docs/SPONSORS.md` (the
claims source of truth) found and fixed this drift:

- Zapier line said "receipt + renewal reminder" → now "receipt
  email + ledger sheet" (the app-side webhook event; the live Zap
  is an account step, not claimed).
- APIMatic line said "PayPal API context" → now "context document
  for Solvia's own API" (`docs/apimatic-context.md`).
- Payments line claimed a "PayPal Agent Toolkit" → removed; the
  app calls the Orders API directly over HTTPS (see
  `src/Solvia/Services/PayPalService.cs`).
- College-search line implied Elastic + ELSER run the demo search
  → now states the built-in search ships by default, the Elastic
  provider sits behind the same config seam, and ELSER did not run
  and is not claimed.
- Render line implied a hosted copy exists → now "planned, not
  yet deployed. There is no hosted URL yet."
- AG Grid line now names the edition that actually ships:
  Community (free).
- KERNEL line now matches the shipped artifact: the portable
  fee-refresh script in `tools/fee-refresh/`; KERNEL is its
  production runner in the plan.
- The "Run it locally (DRAFT)" section (which literally contained
  "exact command: TBD") was replaced with the final steps below.
- `.env.example`'s header no longer suggests copying values into a
  `.env` file — the app reads environment variables only and never
  reads a `.env` file (README Step 3 now says exactly that).

## Steps executed, verbatim from the README

Clone folder: `/tmp/solvia-clean/solvia` (new, empty beforehand).

| README step | Command / action | Observed result |
|---|---|---|
| 1. .NET 10 SDK | `dotnet --version` | `10.0.402` — PASS |
| 2. Get the code | `git clone https://github.com/jlhpacheco/solvia.git` | Clone landed at commit `4f21770e` — PASS |
| 3. PayPal keys (optional) | Skipped, on purpose | The no-keys path is what Step 6 tests. The app started and every page below worked with no keys set — PASS (the not-configured 503 shape for the paid API is covered by the test suite and the Postman collection, `postman/`) |
| 4. Ollama (optional) | Skipped, on purpose | The README promises the built-in fallback reader takes over and "the demo never dies". On the questions step the app labeled its extraction source **fallback** — exactly as documented — PASS |
| 5. Build | `dotnet build src/Solvia.slnx` | **Build succeeded. 0 Warning(s), 0 Error(s)** — PASS |
| 5. Run | `dotnet run --project src/Solvia` | `Now listening on: http://localhost:5156`; the app created `src/Solvia/solvia.db` by itself (SQLite, no server) — PASS |

## Pages walked (README Step 5 table)

| Page | Result |
|---|---|
| `GET /` (home) | 200 |
| `GET /Permits/Apply` (pick your college) | 200 — exactly **25** campus links (`Questions?college=`), matching `data/fees.csv` |
| `GET /College/Fees` (fee table) | 200 — AG Grid adapter script (`feetable.aggrid.js`) and grid markup present |
| `GET /College/Dates` (term dates) | 200 |
| `GET /College/Roster` (college side) | 200 |

## Step 6 — the free path, walked end to end (Priya)

Driven over HTTP exactly as a browser drives it (per-screen form
replay: each POST submits the previous screen's own hidden fields +
antiforgery token; the `solvia_student=priya` cookie selects Priya).

1. `GET /Permits/Questions?college=Modesto Junior College /
   Columbia College / YCCD` → 200.
2. `POST /Permits/Understand` (car in her own words: "Blue 2019
   Honda Civic, plate 8XYZ123", Term permit, Fall) → 200;
   extraction source labeled **fallback** (no Ollama, per Step 4).
3. `POST /Permits/Review` → 200; total shown: **$0 — FREE**.
4. `POST /Permits/Confirm` → 302 to `/Checkout/{id}`.
5. `GET /Checkout/{id}` → "$0.00 — PayPal is not involved."
6. `POST /Checkout/Start/{id}` → `/Checkout/Done/{id}`:
   **"Permit live ✓ — This permit is free at your college — no
   payment was needed."** Permit number **SOL-2026-485647**,
   Term permit, valid 08/24/2026 to 12/22/2026.
7. `GET /Permits/Receipt/{id}` → receipt shows the public check
   link ("Anyone can check this permit: /verify?t=…").
8. `GET /verify?t=…` (no login, no cookie) → **✔ VALID** —
   "This parking permit is real and live." Permit SOL-2026-485647,
   Modesto Junior College / Columbia College / YCCD, student
   Priya, valid August 24, 2026 to December 22, 2026.

**Verdict: PASS.** A machine that never saw this project reached a
live, publicly verifiable permit from the repo + README alone,
with no PayPal keys and no Ollama — the two optional steps skipped
exactly as the README allows.

## Environment notes (sandbox limits — not README failures)

- The walk ran in the build sandbox, where a long-running app
  process cannot open direct TCP to localhost services (Ollama /
  PayPal sandbox). That is precisely why the walk uses the
  documented optional-skip paths: fallback extraction (Step 4) and
  the $0 free college (Step 6, zero PayPal calls by design). On a
  normal machine the same README steps run Ollama locally and the
  paid checkout against the PayPal sandbox with Step 3's keys.
- One test-harness correction during the walk (not an app or
  README issue): ASP.NET antiforgery needs its cookie carried
  alongside the student cookie, so the driver used a real cookie
  jar — what any browser does automatically. First attempt without
  the jar correctly received HTTP 400 from antiforgery validation.
- Known cross-reference for T10: the full suite state at T9 start
  was 72/72 green (per track reports); T9 re-verified the build
  from the fresh clone (0 errors) rather than re-running the suite
  in this sandbox, where the vstest loopback is unavailable.

## Bug filed to another track (not fixed by T9)

- `docs/USE-CASE.md` line 47 still says "Zapier step: receipt +
  renewal reminder" — the same drift fixed in the README. Per
  SPONSORS.md the Zap is receipt email + ledger sheet. Owner: the
  docs/T7 track; T9 owns only the README run section,
  `.env.example`, and this log.

---

## REDO clean-install runs — Oct 18, 2026

The T9 log above stands as the v1 record, unedited. Below is the REDO edition of this log in full: the independent clean-install runs against the rebuilt tree, including the late-evening re-check entry that closed the Step 5b failure (pristine copy, README steps only, no configuration knobs — app builds, boots, and walks Priya's free path to a VALID public verify).

# CLEAN-INSTALL LOG — REDO edition

**Checker:** R10 (independent; built nothing in this tree)
**Date:** Oct 18, 2026 (evening, PDT)
**Tree under test:** `solvia-redo/` as delivered by tracks R0–R9,
copied pristine (no `bin/`, no `obj/`, no `*.db`) to
`/tmp/r10-clean/` so nothing the builders left behind could help.
**Machine:** this sandbox VM — .NET SDK 10.0.402
(`~/workspace/dotnet10/dotnet`), **no PayPal keys in the
environment, no Ollama running** (verified: `curl
http://localhost:11434` → no answer).

The steps followed are the README's own steps, word for word.

---

## Step 1–2 — SDK + code

- `dotnet --version` → 10.0.402 ✔
- Pristine copy in place of the clone; commands run from its root,
  as the README directs.

## Step 5a — Build: **PASS**

```
$ dotnet build src/Solvia.slnx
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Step 5b — Run, exactly as the README writes it: **FAIL**

```
$ dotnet run --project src/Solvia
...
Unhandled exception. System.IO.FileNotFoundException: College fee data file not found: 'data/fees.csv'. Set FEES_CSV_PATH (or FeesCsvPath) to the repo's data/fees.csv.
File name: 'data/fees.csv'
   at Solvia.Services.FeeCatalogService..ctor(String csvPath) in src/Solvia/Services/FeeCatalogService.cs:line 40
   at Program.<>c.<<Main>$>b__0_1(IServiceProvider sp) in src/Solvia/Program.cs:line 82
```

**What is wrong (checker's diagnosis, from the code):**
`dotnet run --project src/Solvia` executes with the project folder
as the working directory (proof: the run created its `solvia.db`
at `src/Solvia/solvia.db`). The redo's `Program.cs` hands the
configured `FeesCsvPath` — the relative string `data/fees.csv` —
straight to `FeeCatalogService`, which resolves it against the
working directory, so it looks for `src/Solvia/data/fees.csv` and
the app dies before it ever listens. The frozen v1 tree anchored
this path (`Path.Combine(builder.Environment.ContentRootPath,
"..", "..", solvia.FeesCsvPath)` in its Program.cs); the redo
dropped the anchor. The database connection string has the same
relative-path shape, which is why the stray DB landed in the
project folder.

**Filed against:** R0 (Program.cs registration) / R1
(FeeCatalogService). **The fix is theirs; the checker changed no
app code.** One line of path anchoring restores v1's behavior.

## Evidence run — same pristine copy, documented knob set

So the rest of the verification could proceed, the identical copy
was booted once more with the one documented configuration knob
the app's own error message and `.env.example` name —
`FEES_CSV_PATH=<absolute path to the copy's data/fees.csv>` —
and nothing else different. The app booted, listened on
http://localhost:5156, and stayed up for the whole walk. This run
proves the application logic; it does **not** rescue the
as-documented run above, which is the one a judge would hit.

## Step 6 — Priya's free path, end to end: **PASS** (on the evidence run)

Forms replayed from their own hidden antiforgery fields, cookie
jar carried through; the verify step used a fresh request with
**no cookies** (anonymous), as a member of the public would.

| # | Step | Result |
|---|---|---|
| 1 | GET `/` | 200, demo switcher lists the six fictional people, Maria default |
| 2 | POST `/Home/SwitchStudent` id=priya | 302 → `/` ✔ |
| 3 | GET `/Permits/Apply` as Priya | 200 — **"Add a vehicle first"** card (she has no car yet) ✔ |
| 4 | POST `/Vehicles/Create` (plate 9PRIYA1, CA, Honda, Civic via "Other — type it", Blue) | 302 → `/Vehicles`; Apply now lists the car as **"9PRIYA1 · CA · Honda Civic"** ✔ |
| 5 | POST `/Permits/Apply` (Modesto Junior College / Columbia College / YCCD, Term permit, Fall, start = the form's own default date) | **302 → `/Permits/My`** — issued at the POST; the checkout page never rendered ✔ |
| 6 | GET `/Permits/My` | 200 — tiles (Total permits / Paid / Pending payment), row shows **SOL-2026-584338**, **"Not Required"** spelled out, Receipt + Permit (PDF) actions ✔ |
| 7 | GET `/Permits/Receipt/{id}` | 200 — permit number + public check link ✔ |
| 8 | GET the check link `/verify?t=…` **anonymously** | 200 — **✔ VALID**, permit number, type, category (Student), privilege line ("Student/Public Parking Eligible"), payment word ("Not Required") ✔ |
| 9 | GET `/api/verify?t=…` (JSON twin) | 200 — `{"found":true,"isValid":true,...,"plateState":"CA",...}` — same verdict and fields, plate itself never exposed ✔ |
| 10 | GET `/Permits/PermitPdf/{id}` | 200 `application/pdf`, body starts `%PDF-` ✔ |
| 11 | Database read-back | one row: YCCD · Category **Student** (snapshot) · PriceCents **0** · PaymentStatus **NotRequired** · ReceiptNumber **SOL-2026-584338** · Source **form** ✔ |

No PayPal keys were set and none were needed; no Ollama was
running and none was needed. Nothing was hand-inserted: every row
came through the app's own forms and services.

---

## Verdict for this log

- Build from a pristine copy, README only: **PASS (0/0)**.
- Priya's YCCD free path to a VALID public verify, no keys, no
  Ollama: **PASS** — on the evidence run described above.
- Run from a pristine copy **exactly as the README writes it**:
  **FAIL** — the app crashes on the un-anchored relative
  `data/fees.csv` path before listening (verbatim exception
  above). Per REDO-PLAN §4 step 2, a clean-install log must be
  *clean* for the swap; this one is not. **The swap is blocked on
  this item** until the owning track anchors the path and this
  log's Step 5b is re-run green from a pristine copy.

---

## RE-CHECK RUN — Oct 18, 2026 (late evening, PDT) — Step 5b re-run green

**Re-checker:** independent fresh eyes (not R10, not a builder;
nothing fixed, no app or test code changed).
**Tree under test:** a NEW pristine copy of `solvia-redo/`
(source only — no `bin/`, no `obj/`, no `*.db`; 206 files) at
`~/workspace/paypal-hackathon/recheck-scratch/`, on the home
filesystem per the Lyceum `/tmp` tmpfs lesson. Copy deleted after
the run.
**Environment:** `FEES_CSV_PATH`, `FeesCsvPath`,
`PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PayPal__ClientId`,
`PayPal__ClientSecret`, `PayPal__WebhookId`,
`ConnectionStrings__SolviaDb`, `ASPNETCORE_URLS` — all
explicitly **UNSET**. No PayPal keys, no Ollama. SDK 10.0.402,
same machine as above. README steps followed word for word,
from the copy's root.

### Step 5a — Build: **PASS**

```
$ dotnet build src/Solvia.slnx
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Step 5b — Run, exactly as the README writes it: **PASS**

```
$ dotnet run --project src/Solvia
...
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5156
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

No `FileNotFoundException` anywhere in the boot log — the
Finding 1 crash does not reproduce. `GET /` → **200**, demo
switcher listing the six fictional people. No configuration
knob of any kind was set for this run: the anchored path finds
the copy's own `data/fees.csv` on its own.

### Step 6 — Priya's free path, end to end: **PASS** (on this knob-free run)

Forms replayed from their own hidden antiforgery fields, cookie
jar carried through; the verify step used a fresh request with
**no cookies** (anonymous).

| # | Step | Result |
|---|---|---|
| 1 | GET `/` | 200, demo switcher lists the six fictional people |
| 2 | POST `/Home/SwitchStudent` id=priya (her own form) | ✔ current person = Priya |
| 3 | GET `/Permits/Apply` as Priya | 200 — **"Add a vehicle first"** card ✔ |
| 4 | POST `/Vehicles/Create` (plate 9RECHK1, CA, Honda, Civic via "Other — type it", Blue) | → `/Vehicles`; vehicle saved ✔ |
| 5 | POST `/Permits/Apply` (Modesto Junior College / Columbia College / YCCD, Term permit, Fall, start = the form's own default date) | lands on **`/Permits/My`** — issued at the POST; no checkout rendered ✔ |
| 6 | GET `/Permits/My` | permit **SOL-2026-885833**, **"Not Required"** spelled out ✔ |
| 7 | GET `/Permits/Receipt/{id}` | 200 — carries the permit's public check link ✔ |
| 8 | GET the check link `/verify?t=…` **anonymously** | 200 — **VALID** ✔ |

### Supporting checks from the same re-check

- `LICENSE` at the redo root — SHA-256
  `2c73a01d6dfb115c5914c9d69611c7af18d2a6f73b575c7fb27fb53350182874`,
  identical to the frozen tree's LICENSE.
- `data/fees.csv` — SHA-256
  `327aca14c33f99a1ffd412ccdbb8ec434b02c1cb440c17d2e5ce5edfabe5771c`,
  unchanged.
- Full suite on the final tree (re-checker's own run,
  copied-folder xUnit console runner): **Total 180, Failed 0**.

### Verdict for this log

The Step 5b **FAIL** recorded above is **closed**: from a
pristine copy, with the README's steps only and no knobs set,
the app builds, boots, and walks Priya's free path to a VALID
public verify. This log is now *clean*, and the clean-install
gate no longer blocks the swap.
