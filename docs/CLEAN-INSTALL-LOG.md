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
