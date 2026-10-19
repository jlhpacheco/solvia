![Solvia logo](assets/logo.webp)

# Solvia

> **DRAFT — PayPal AI Hackathon project, in preparation. Not yet submitted.**

**Solvia** — a simple helper that lets a college student pay what is due for her vehicle permit, fast, with PayPal.

Latest review: [changes and verification](docs/REVIEW-2026-10-19.md),
[hackathon readiness and judging](docs/HACKATHON-READINESS.md),
[design direction](docs/DESIGN-SYSTEM.md), and
[printable student and employee permits](docs/PRINTABLE-PERMITS.md).
Repeatable smoke/browser checks: [TypeScript + Playwright testing](docs/TESTING.md).

## The name

Latin:

- **solvo** = "I pay, I settle what is due"
- **via** = "road" — the road a vehicle travels

Together: *pay what is due for the vehicle on the road.*

## Simple use case

Maria is a fictional student. It is her first day.

1. Maria opens Solvia and picks how to start:
   - **Ask Solvia (the AI door):** the AI asks three questions only: **School? Car? Term?**
   - **The classic form:** pick campus, car, permit type, and start date — the same shape colleges already use.
2. Both doors build the **same application** through the same brain, and both show the **exact total first**, taken from a real fee table (see `data/fees.csv` — 10 campuses, every one publishing its student semester price, from the 2026-10-18 scan of 25 mycampuspermit.com pages; only the 10 with a published price ship, and no price is ever guessed).
3. Maria approves the total.
4. **PayPal Sandbox pays.** Test money only — no real money, no real student data.
5. Maria gets a receipt with the PayPal transaction ID and a downloadable demo permit PDF. This prototype does not grant real campus parking authorization.
6. Anyone can check the permit, no login, at its public link (`/verify?t=…`). The college sees a roster: who paid, who didn't.

Two more fictional people show the edges: **Elena** is a college employee — the demo policy exempts employees, so her permit is issued free at Apply. Actual campus employee fees and eligibility differ. **Pat** is a member of the public and pays the catalog fee in this demo; this does not reproduce every campus's affiliation rules.

Full flow: [docs/USE-CASE.md](docs/USE-CASE.md)

## Tools used

| Part | Tool |
|---|---|
| Payments (central) | PayPal Sandbox — Orders API (create order → student approves → capture), called directly over HTTPS from the app; a signature-verified PayPal webhook is the second road to the same truth. Paid lands only from a COMPLETED capture — never from a return page |
| AI (central) | **Phi-4-mini** — a simple open model, run locally via **Ollama** (MIT-licensed model; structured extraction of school / car / term — the extraction has no price field, so the fee table, never the model, sets the price) |
| Fee table | **AG Grid Community edition** (free, sponsor) — the 10-college fee table, with a built-in table renderer behind the same seam showing identical values |
| Printable permits | **PDFsharp 6.2.3 (MIT)** with embedded **Roboto (Apache-2.0)** — US Letter student and employee permits; no separate PDF application or OS font installation required |
| College search | Built-in deterministic search ships by default; an **Elastic** (sponsor) provider sits behind the same config seam — the live Elastic node was not run for this build and ELSER is not claimed — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Term calendar | Native calendar list (built-in) — term start, permit end, renewal date. Bryntum is **not** used: it has no free edition, and its 45-day trial would expire during judging — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Hosted copy | **Render** (sponsor) — **planned, not yet deployed.** There is no hosted URL yet; judges run it locally with the steps below |
| Workflow sponsors | **APIMatic** (context document for Solvia's own API — [docs/apimatic-context.md](docs/apimatic-context.md)), **Postman** (sandbox flow collection — saved in [postman/](postman/)), **Zapier** (payment-complete webhook in the app → receipt email + ledger sheet; the live Zap is created in your own Zapier account — [docs/zapier-recipe.md](docs/zapier-recipe.md)), **KERNEL** (fee-refresh job — [tools/fee-refresh/](tools/fee-refresh/); the portable script ships, KERNEL is its production runner in the plan) |

Sponsors are used only where they do a real job in this app or its workflow. Forced fits were dropped.

### AI model choice

**Microsoft Phi-4-mini**, served through Ollama, is the configured model. The default endpoint is local to the **application host**, which may be a server rather than the student's computer. The project selects Microsoft's model; Qwen is not used. Model extraction produces permit details, never a price. The deterministic fallback keeps the form usable but does not prove live AI operation.

## Privacy — why our AI is built this way

Use fictional people and vehicles for this public demo. Browser submissions reach the app server; a remote Ollama endpoint would receive extraction inputs. Solvia implements inference, not model training, and makes no institutional compliance certification. See [docs/PRIVACY.md](docs/PRIVACY.md) for the actual data paths.

## Rules compliance — PayPal AI Hackathon

- [ ] Record **live PayPal and AI evidence** — integrations ship, but a real sandbox buyer approval/capture and an Ollama inference must be verified for the final demo. The fee catalog sets the total.
- [x] **PayPal Sandbox only** — fictitious test accounts, mock transactions, no real money, no real student data (Maria is fictional).
- [x] **Locally runnable prototype** — tested routes and services; complete the external-service checks in the readiness document before submission. Render hosting is planned.
- [x] **Public repository with an open-source license** — this repo is public, MIT License at the top level.
- [x] **Tools are listed and explained** — see the table above.
- [ ] **Disclose prior work** — Solvia follows the design of the author's earlier CarPermitApp. Describe that starting point and the substantial changes made during the submission period; existing projects are allowed subject to the official rules. Credentials and old app configuration are not copied into this repository.
- [ ] Demo video: under 3 minutes, public on YouTube, showing the app working end to end — **to be recorded**.
- [ ] Submission text in English — this README and the Devpost text.
- [ ] Demo stays free for judges to test through the judging period (Dec 1–15, 2026).

Deadline: **Nov 12, 2026, 12:00 PM Pacific Time.**
Eligibility, registration, team details, and the actual Devpost submission remain participant checks. See the [official rules](https://paypalaihackathon.devpost.com/rules).

## Run it locally

You need one thing installed first. Everything else is optional.
These steps are what the checker executed from a pristine copy;
that run is recorded in [docs/CLEAN-INSTALL-LOG.md](docs/CLEAN-INSTALL-LOG.md).

### Step 1 — Install the .NET 10 SDK (the only requirement)

Download the free .NET 10 SDK from
https://dotnet.microsoft.com/download/dotnet/10.0 and install it.
Then check it worked:

```
dotnet --version
```

You should see a version starting with `10.`

### Step 2 — Get the code

```
git clone https://github.com/jlhpacheco/solvia.git
cd solvia
```

Run every later command from this folder (the repo root) — the app
finds its fee table at `data/fees.csv` relative to it.

### Step 3 — (Optional) Add your free PayPal Sandbox keys

Without keys the app still runs: you can open every page, and the
free-college walk in Step 6 works end to end with no PayPal at all.
Keys are only needed to walk a *paid* college through the real
sandbox checkout. To get keys (free — test money only):

1. Go to https://developer.paypal.com and sign in. (Create a free
   developer account first if you don't have one.)
2. Open **Apps & Credentials**.
3. Select the **Sandbox** toggle.
4. Click **Create App**, give it any name (for example `solvia`),
   and create it.
5. PayPal shows a **Client ID** and a **Secret**. Copy both.

Then set them as environment variables in the same terminal where
you will run the app. The app reads environment variables only —
it never reads a `.env` file, and no secret is ever committed
(`.env.example` at the repo root lists every variable name).

Windows (PowerShell):

```
$env:PAYPAL_CLIENT_ID = "paste your sandbox client id"
$env:PAYPAL_CLIENT_SECRET = "paste your sandbox secret"
```

Mac / Linux (bash):

```
export PAYPAL_CLIENT_ID="paste your sandbox client id"
export PAYPAL_CLIENT_SECRET="paste your sandbox secret"
```

For the approval click in the paid flow, PayPal also gives you free
sandbox test accounts: developer.paypal.com → **Sandbox →
Accounts**. The **Personal** account is the buyer; its email and
password are shown there. Test money only, never real money.

### Step 4 — (Optional) The local AI: Ollama + Phi-4-mini

The default AI endpoint runs on the same host as Solvia. To use the real model on your development computer:

1. Install Ollama from https://ollama.com
2. Pull the model once (about 2.5 GB):
   ```
   ollama pull phi4-mini
   ```
3. Leave Ollama running — it serves the model at
   http://localhost:11434.

**You can skip this step.** If Ollama is not running, Solvia uses a
built-in fallback reader that understands the same answers
(school / car / term) with simple rules — the demo never dies
because the AI is off, the screens work the same either way, and
every answer is labeled honestly with which reader produced it
(`ollama` or `fallback`).
For the hackathon demo, verify an actual `ollama` response: fallback alone is not evidence of AI use.

### Step 5 — Build and run

```
dotnet build src/Solvia.slnx
dotnet run --project src/Solvia
```

The build should finish with 0 errors. On first run the app
creates its own small local database file (`solvia.db`, SQLite —
there is no database server to install).

Open the address the app prints — **http://localhost:5156**:

| Page | What it is |
|---|---|
| http://localhost:5156/ | Home — pick which fictional person you are (Maria is the default) |
| http://localhost:5156/Permits/Ask | The AI door: three questions (school / car / term) |
| http://localhost:5156/Permits/Apply | The classic form door: campus, your car, permit type, start date |
| http://localhost:5156/Permits/My | My Permits — your applications, receipts, and permit PDFs |
| http://localhost:5156/Vehicles | Your vehicle garage — add / edit your cars |
| http://localhost:5156/College/Fees | The 10-college fee table (AG Grid Community) |
| http://localhost:5156/College/Dates | Term dates — term start, permit end, renewal date |
| http://localhost:5156/College/Roster | The college side: who paid, who hasn't |
| The check link on any receipt (`/verify?t=…`) | Anonymous public permit check — answers VALID or not |

### Step 6 — Try the free path (no keys, no Ollama needed)

1. On the home page, switch the person to **Priya**.
2. Open **Apply**. Priya has no car yet, so Solvia first shows an
   "Add a vehicle first" card — add any car (plate, state, make,
   model, color).
3. Back on **Apply**, pick **Modesto Junior College / Columbia
   College / YCCD** — permits there are FREE — permit type
   **Term permit**, any start date inside the term, and submit.
4. A $0 permit is issued on the spot: no checkout page, no PayPal.
   You land on **My Permits** with a permit number.
5. Open the permit's check link — it answers **VALID**, no login.

Try **Elena** the same way at any campus: she is an employee, so
her permit is issued free at Apply at every college, and her PDF
says **EXEMPT (Employee)**.

## Why PayPal

The Orders flow maps to a permit purchase: show the catalog total, let the
buyer approve it, capture through PayPal, then issue the receipt. The server
checks the capture status, amount, currency, and ID before marking it Paid.
AI drafts permit details; the person approves payment and the catalog sets
the price.

Sandbox authentication and a $30 order creation/reuse have been verified
through the running app. Buyer approval and capture are still pending. The
recorded demo must show those steps completing rather than relying on test
doubles. See [the testing record](docs/TESTING.md).

## Data note

`data/fees.csv` is research data scanned 2026-10-18 from the public pages of mycampuspermit.com (Parking Management Bureau), the incumbent permit store for these campuses. The shipped table holds exactly the 10 campuses that published a student semester price in that scan of 25 PMB pages; the other 15 published none and are not in the app. Every shipped row has a price (FREE is a price) — we never filled a price in.

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Jose Luis Pacheco.

Third-party components and their licenses: [NOTICES.md](NOTICES.md).
