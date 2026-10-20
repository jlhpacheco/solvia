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
Baseline viability: [Stage One implementation evidence](docs/STAGE-ONE-EVIDENCE.md).
Impact, fair competitor comparison and proposed campus pilot:
[evidence and differentiation](docs/IMPACT-AND-DIFFERENTIATION.md).

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
| AI adapter | **Together AI with Qwen/Qwen3.5-9B** is the default hosted reader; **Phi-4-mini via Ollama** is optional. Both draft vehicle details for human review through shared schema/plate safeguards. The catalog sets fees. Eight real Together application cases passed, including the final release rerun, with `source=together`; fallback is not counted as model evidence. |
| Fee table | **AG Grid Community edition** (free, sponsor) — the 10-college fee table, with a built-in table renderer behind the same seam showing identical values |
| Printable permits | **PDFsharp 6.2.3 (MIT)** with embedded **Roboto (Apache-2.0)** — US Letter student and employee permits; no separate PDF application or OS font installation required |
| College search | Built-in deterministic search ships by default; an **Elastic** (sponsor) provider sits behind the same config seam — the live Elastic node was not run for this build and ELSER is not claimed — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Term calendar | Native calendar list (built-in) — term start, permit end, renewal date. Bryntum is **not** used: it has no free edition, and its 45-day trial would expire during judging — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Hosted copy | **Render** (sponsor) — **planned, not yet deployed.** There is no hosted URL yet; judges run it locally with the steps below |
| Supporting artifacts and plans | An [APIMatic context document](docs/apimatic-context.md), [Postman collection](postman/), [Zapier recipe](docs/zapier-recipe.md), and portable [fee-refresh script](tools/fee-refresh/) ship. Executing APIMatic, a live Zap, or KERNEL hosting is not verified. These artifacts are not additional completed sponsor integrations. |

**AG Grid Community is the demonstrated sponsor integration.** The Elastic adapter and other supporting artifacts remain separate from verified external-service use; see [sponsor status](docs/SPONSORS.md).

Our prize goal is a Grand Prize plus the AG Grid sponsor category where the
rules permit that combination. Actual Community grid use is demonstrated;
AG Grid Studio, AG Charts and executed APIMatic use are not claimed. Prize
eligibility and selection remain organizer decisions.

### AI model choice

**Together AI with `Qwen/Qwen3.5-9B` is the default.** `AI_PROVIDER=together`
selects its hosted API; optional `AI_PROVIDER=ollama` uses Phi-4-mini at the
configured endpoint. Validated sources are `together`, `ollama`, or
`fallback`. The shared contract preserves explicit/ambiguous plate rules and
has no price field. People review the draft; the catalog supplies the fee.

Together lists this model among its [small and fast recommendations](https://docs.together.ai/docs/inference/recommended-models).
Our server requests structured JSON with reasoning disabled, temperature 0,
and a 512-output-token cap. The final eight-case application run observed a
median **2.4 seconds** from submit to review, range **1.6–5.3 seconds**, on
this computer. This includes the application flow, not just provider latency,
and is not a performance guarantee.
Current [published prices](https://docs.together.ai/docs/serverless/models)
are $0.17 per million input tokens and $0.25 per million output tokens.
An illustrative 1,000-input/200-output-token request costs **$0.00022** in
inference tokens; this is an estimate, not a measured bill or a fixed cost
per permit. Include instructions and schema in input-token estimates.

Together's key is privately configured in Development user-secrets. The same
eight live cases passed twice, including a final **8/8** run on application
source `e3a4993` after credential rotation and deadline hardening. These are
eight distinct cases, all labeled `together`. Historically,
a direct warmed CPU Phi-4-mini request returned in **32.26 seconds**, while
the app's **10-second** budget fell back. See
[provider-aware opt-in checks](docs/TESTING.md#opt-in-live-ai-checks).

## Privacy — why our AI is built this way

Use fictional adult (18+) people and vehicles for this demo. Browser answers
reach the app server and its selected model endpoint. Default Together mode
sends the sentence to a hosted service. Provider storage/training settings
and residency limits are described in [privacy](docs/PRIVACY.md); no account
privacy setting or zero-retention guarantee is claimed.

## Rules compliance — PayPal AI Hackathon

- [x] **Verify a real PayPal Sandbox capture** — the running app captured a $30.00 USD Fresno City College term permit and produced a paid receipt/PDF. An independent provider query confirmed `COMPLETED` and the exact amount. This used PayPal's official Sandbox test-card confirmation followed by Solvia's capture endpoint; wallet/Personal-buyer UI approval has not been verified.
- [x] Record **live application AI evidence** — eight Together cases passed with `source=together` in every case, including the final release rerun after credential rotation and deadline hardening. This is separate from the controlled browser suite; the catalog sets the total.
- [x] **PayPal Sandbox only** — fictitious test accounts and test transactions, no real money, no real student data (Maria is fictional).
- [x] **Locally runnable prototype** — tested routes and services; complete the external-service checks in the readiness document before submission. Render hosting is planned.
- [x] **Public repository with an open-source license** — this repo is public, MIT License at the top level.
- [x] **Tools are listed and explained** — see the table above.
- [x] **New project chronology confirmed** — the participant confirmed Solvia was first built during this hackathon, after October 1, 2026. Solvia is an independent implementation and business model; its commits document development within the event.
- [ ] Demo video: under 3 minutes and public on YouTube. A 156.086-second actual-flow raw recording is complete; the narrated/captioned MP4 is complete at 156.1 seconds; the public URL remains pending.
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

#### Default hosted reader: Together AI + Qwen3.5-9B

Create/sign in to a [Together account](https://api.together.ai/) and provision
a Together API key privately in the server's secret environment as
`TOGETHER_API_KEY`, or in Development .NET user-secrets as `Together:ApiKey`.
For Development setup, run the hidden-prompt helper from the repository root:

```powershell
.\scripts\Set-TogetherCredentials.ps1
```

It selects Together/model/timeout and sends the key through stdin to .NET
user-secrets, keeping it out of command arguments.
Never put a real key in chat, command arguments, logs, screenshots, browser
code or Git. Development user-secrets are per-user local configuration,
**not an encrypted vault**.

Set these non-secret settings in the terminal that runs Solvia:

```powershell
$env:AI_PROVIDER = 'together'
$env:TOGETHER_MODEL = 'Qwen/Qwen3.5-9B'
$env:TOGETHER_TIMEOUT_SECONDS = '10'
```

Restart Solvia after changing server configuration. The default timeout is
10 seconds, with configured values clamped to 1–90. Missing credentials,
timeouts and unusable answers produce a labeled fallback. The adapter accepts
only the pinned `Qwen/Qwen3.5-9B` model; an unsupported ID also falls back.
Together is metered. The development key is configured privately and the
eight live application cases passed, including the final release rerun. Read
[privacy](docs/PRIVACY.md) before sending inputs and
the provider's [structured-output contract](https://docs.together.ai/docs/inference/chat/structured-outputs)
for the hosted request format.

#### Optional local reader: Ollama + Phi-4-mini

To run the model on your development computer:

1. Install Ollama from https://ollama.com.
2. Pull the model once (about 2.5 GB):
   ```
   ollama pull phi4-mini
   ```
3. Leave Ollama running at http://localhost:11434 and select
   `AI_PROVIDER=ollama` in Solvia's process before restarting.

The default Ollama endpoint is local to the app host, which may be a server.
Its request budget defaults to 10 seconds, including loading;
`OLLAMA_TIMEOUT_SECONDS` clamps to 1–90. A longer budget is a deliberate
local testing choice; measure the actual app response before recording a demo.

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
controlled desktop/phone run passed **84/84 checks in 4.0 minutes**, including
**32 smokes**. The subsequent receipt-view validity correction is covered by
the final backend's rendered cases; no full browser rerun of that delta is
claimed. A separate 16/16 tablet run passed in 2.3 minutes on `eb488ab1`,
covering Chromium and WebKit in portrait/landscape. Together the two suites
cover 100 distinct controlled browser checks: 84 desktop/phone, including
32 smokes, plus 16 tablet cases. The eight live AI cases remain separate.
The first tablet run passed 12/16: two harness navigation races and two real
WebKit My permits overflow failures were corrected by waiting for person
selection and adjusting the responsive table/card breakpoint before rerunning.
The final backend run passed **390/390 in 12 seconds**, zero failed/skipped,
on source `92cba211`. It includes shared AI quotas/concurrency and neutral
payment-versus-validity messaging. Paid denied/future/expired permits cannot
be called live merely because payment completed. AI extraction is limited
to 20 requests/client/minute and 60 globally, with two concurrent/client and
four globally; these process-local guards are not a provider billing cap.
Historical 371-test and 82-case aggregate checkpoints, plus the earlier
30/30 smoke run on `e3a4993`, remain documented. Eight distinct real Together
cases passed twice on the earlier model-tested source and remain separate
from the controlled browser count; no new live suite on `92cba211` is claimed.
See [test coverage and limits](docs/TESTING.md).
An additional 156.086-second raw recording exercised three actual Together
calls and a separate $30 Sandbox purchase: order `5CV7753505063622B`, capture
`9AP215253S500404B`. Provider lookup/retries confirmed one completed capture;
paid PDF retrieval and anonymous verification privacy were checked. This
separate-database run is excluded from the historical matrix total. Final
narration/rendering and public YouTube publication remain pending.
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


## Review the working flow

[Watch or download the 2:36 demo](docs/review/Solvia-demo.mp4), [read the screenshot guide](docs/review/Solvia-step-by-step-guide.pdf), or [inspect the quality-control process](docs/review/Solvia-quality-control.pdf).

The edited recording shows actual Together inference, one new $30 PayPal Sandbox capture, and the resulting permit PDF. Sandbox card approval uses the explicitly disclosed REST test harness; wallet login is not demonstrated. Brief holds use actual screenshots from that same recording. The My permits screen was recorded before the later tablet card-layout refinement. All profiles are fictional. A public YouTube upload remains a submission gate.
