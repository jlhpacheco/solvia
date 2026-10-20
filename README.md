![Solvia logo](assets/logo.webp)

# Solvia

> **DRAFT — PayPal AI Hackathon project, in preparation. Not yet submitted.**

**Solvia** — a simple helper that lets a college student pay what is due for her vehicle permit, fast, with PayPal.

Latest review: [changes and verification](docs/REVIEW-2026-10-19.md),
[hackathon readiness and judging](docs/HACKATHON-READINESS.md),
[design direction](docs/DESIGN-SYSTEM.md), and
[printable student and employee permits](docs/PRINTABLE-PERMITS.md).
Repeatable smoke/browser checks: [TypeScript + Playwright testing](docs/TESTING.md).
Review and regression workflow: [quality control](docs/QUALITY-CONTROL.md).

## The name

Latin:

- **solvo** = "I pay, I settle what is due"
- **via** = "road" — the road a vehicle travels

Together: *pay what is due for the vehicle on the road.*

## Simple use case

Maria is a fictional adult student (18+). It is her first day.

1. Maria opens Solvia and picks how to start:
   - **Ask Solvia (the AI door):** choose a college, describe a car, and choose a permit. The configured reader drafts vehicle fields for review.
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
| AI adapter | **Phi-4-mini via Ollama** is the default; **Meta Model API Standard Muse Spark 1.3** is an explicit optional hosted provider. Both draft vehicle details for human review through the shared schema/plate safeguards. The catalog sets fees. Application-level live-model proof remains pending; a labeled fallback is not AI evidence. |
| Fee table | **AG Grid Community edition** (free, sponsor) — the 10-college fee table, with a built-in table renderer behind the same seam showing identical values |
| Printable permits | **PDFsharp 6.2.3 (MIT)** with embedded **Roboto (Apache-2.0)** — US Letter student and employee permits; no separate PDF application or OS font installation required |
| College search | Built-in deterministic search ships by default; an **Elastic** (sponsor) provider sits behind the same config seam — the live Elastic node was not run for this build and ELSER is not claimed — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Term calendar | Native calendar list (built-in) — term start, permit end, renewal date. Bryntum is **not** used: it has no free edition, and its 45-day trial would expire during judging — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Hosted copy | **Render** (sponsor) — **planned, not yet deployed.** There is no hosted URL yet; judges run it locally with the steps below |
| Supporting artifacts and plans | An [APIMatic context document](docs/apimatic-context.md), [Postman collection](postman/), [Zapier recipe](docs/zapier-recipe.md), and portable [fee-refresh script](tools/fee-refresh/) ship. Executing APIMatic, a live Zap, or KERNEL hosting is not verified. These artifacts are not additional completed sponsor integrations. |

**AG Grid Community is the demonstrated sponsor integration.** The Elastic adapter and other supporting artifacts remain separate from verified external-service use; see [sponsor status](docs/SPONSORS.md).

### AI model choice

**Ollama/Phi-4-mini remains the default.** Its endpoint is local to the
application host unless configured otherwise. Optional `AI_PROVIDER=meta`
selects server-side Meta Model API Standard `muse-spark-1.3`; inputs then leave
the app host for Meta. Validated sources are `ollama`, `meta`, or `fallback`.
The shared contract preserves explicit/ambiguous plate rules and has no price
field. People review the draft; the catalog supplies the fee.

Current evidence: a direct warmed CPU Phi-4-mini request returned in **32.26
seconds**, while the app's default **10-second** budget fell back. No Meta key
is configured and no live Meta request has been verified. See
[provider-aware opt-in checks](docs/TESTING.md#opt-in-live-ai-checks).

## Privacy — why our AI is built this way

Use fictional adult (18+) people and vehicles for this demo. Browser answers
reach the app server and its selected model endpoint. Hosted Meta use has
additional age and data-processing terms; see [privacy](docs/PRIVACY.md).

## Rules compliance — PayPal AI Hackathon

- [x] **Verify a real PayPal Sandbox capture** — the running app captured a $30.00 USD Fresno City College term permit and produced a paid receipt/PDF. An independent provider query confirmed `COMPLETED` and the exact amount. This used PayPal's official Sandbox test-card confirmation followed by Solvia's capture endpoint; wallet/Personal-buyer UI approval has not been verified.
- [ ] Record **live application AI evidence** — verify the selected provider's validated `ollama` or `meta` result. A direct slow CPU response or `fallback` label does not close this check. The catalog sets the total.
- [x] **PayPal Sandbox only** — fictitious test accounts and test transactions, no real money, no real student data (Maria is fictional).
- [x] **Locally runnable prototype** — tested routes and services; complete the external-service checks in the readiness document before submission. Render hosting is planned.
- [x] **Public repository with an open-source license** — this repo is public, MIT License at the top level.
- [x] **Tools are listed and explained** — see the table above.
- [x] **New project chronology confirmed** — the participant confirmed Solvia was first built during this hackathon, after October 1, 2026. Solvia is an independent implementation and business model; its commits document development within the event.
- [ ] Demo video: under 3 minutes, public on YouTube, showing the app working end to end — **to be recorded**.
- [ ] Submission text in English — this README and the Devpost text.
- [ ] Demo stays free for judges to test through the judging period (Dec 1–15, 2026).

Deadline: **Nov 12, 2026, 12:00 PM Pacific Time.**
Eligibility, registration, team details, and the actual Devpost submission remain participant checks. See the [official rules](https://paypalaihackathon.devpost.com/rules).

## Run it locally

The free/fallback preview requires the .NET 10 SDK. The complete PayPal-plus-AI
journey also needs Sandbox credentials and a working selected model endpoint
as described below. The free preview alone does not establish those integrations.
An earlier clean-copy run is recorded in [docs/CLEAN-INSTALL-LOG.md](docs/CLEAN-INSTALL-LOG.md).

### Step 1 — Install the .NET 10 SDK

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
you will run the app, or use Development-mode .NET user-secrets. The app
does not automatically load a `.env` file; never commit secrets
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

### Step 4 — Select an optional AI provider

#### Default: Ollama + Phi-4-mini

The default AI endpoint runs on the same host as Solvia. To use the real model on your development computer:

1. Install Ollama from https://ollama.com
2. Pull the model once (about 2.5 GB):
   ```
   ollama pull phi4-mini
   ```
3. Leave Ollama running — it serves the model at
   http://localhost:11434.

Use `AI_PROVIDER=ollama` to select this provider explicitly. The model budget
defaults to 10 seconds, including loading; `OLLAMA_TIMEOUT_SECONDS` can set
1–90 seconds (values clamp to that range). A slower response or unusable
answer produces the labeled rules fallback. A longer budget is a deliberate
local testing choice; measure the actual app response before recording a demo.

#### Optional hosted Meta: Standard Muse Spark 1.3

Obtain a Model API credential from the [Meta developer dashboard/docs](https://dev.meta.ai/docs/overview).
From the repository root, use the hidden prompt:

```powershell
.\scripts\Set-MetaCredentials.ps1
```

The script selects Meta and stores `AI:Provider`, `Meta:ApiKey`, `Meta:Model`
and `Meta:TimeoutSeconds` through stdin into per-user .NET Development
user-secrets. This is local configuration, **not an encrypted vault**. Restart
Solvia in Development mode afterward. Do not paste the key into chat, command
arguments, logs, screenshots or Git. A coding-harness subscription credential
is not the application API credential.

For deployment, provision `META_API_KEY` in the server's secret manager/process
environment and set these non-secret settings:

```powershell
$env:AI_PROVIDER = 'meta'
$env:META_MODEL = 'muse-spark-1.3'
$env:META_TIMEOUT_SECONDS = '10'
```

The Meta timeout clamps to 1–90 seconds. The adapter accepts Standard 1.3
(default), 1.2 and 1.1; Contributor model IDs are rejected. Missing keys or
unusable responses fall back visibly. Meta requests are metered. Standard
data handling and adult-only restrictions are described in [privacy](docs/PRIVACY.md)
and the [primary API schema](https://dev.meta.ai/docs/api-reference/chat-completions/schemas).
No live Meta call is claimed at this checkpoint. To return to the default,
set `AI_PROVIDER=ollama` in the app's process before restarting.

Either provider is optional for the free/fallback preview. A successful
provider-labeled application response is required for our live-model evidence.

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
| http://localhost:5156/Permits/Ask | The AI door: college / car description / permit choice |
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

On October 19, the running app completed a **$30.00 USD Sandbox capture**
for a Fresno City College term permit. PayPal order `1G1852006N6969449`
and capture `7WR24202DB5257942` were independently confirmed `COMPLETED`;
Solvia issued receipt `SOL-2026-197973` and a paid PDF containing both IDs.
The verified path used PayPal's official Sandbox test-card confirmation and
Solvia's actual REST capture endpoint. Wallet/Personal-buyer UI approval is
a separate, unverified path. See [the testing record](docs/TESTING.md).
The follow-up Sandbox matrix passed **20 checks**: 16 permit purchases across
four colleges, student/public personas, and day/term permits, plus four
payment guards. It verified 14 completed captures totaling **$945.00 USD in
Sandbox test funds** and two free permits. All 16 PDFs passed content checks.
See [the provider results](docs/SANDBOX-RESULTS-2026-10-19.md).
Two further real-provider checks verified unapproved-order and refused-card
handling. The official PayPal/card buttons were also checked against the
actual remote SDK, together with the no-JavaScript continuation. The latest
controlled browser validation verified 82 distinct desktop/mobile checks,
including 30 smoke checks, across full and corrected targeted runs; 34 SDK
contract checks are included. This is aggregate coverage, not one clean run.
The latest coordinated backend run passed 367/367 tests, superseding earlier
baselines; the testing record retains the initial failure and correction.
See [test coverage and limits](docs/TESTING.md).
For pending order IDs, buyer approval errors, or resuming payment, see
[Sandbox payment troubleshooting](docs/PAYPAL-TROUBLESHOOTING.md).

## Data note

`data/fees.csv` is research data scanned 2026-10-18 from the public pages of mycampuspermit.com (Parking Management Bureau), the incumbent permit store for these campuses. The shipped table holds exactly the 10 campuses that published a student semester price in that scan of 25 PMB pages; the other 15 published none and are not in the app. Every shipped row has a price (FREE is a price) — we never filled a price in.

CSV pricing is the accepted hackathon scope. No live college feed is connected.
Checkout snapshots the catalog fee; an open PayPal order or completed payment
cannot be repriced by a later catalog edit. Each purchase creates its own order,
so a separate hosted payment link for every college and price is unnecessary.

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Jose Luis Pacheco.

Third-party components and their licenses: [NOTICES.md](NOTICES.md).
