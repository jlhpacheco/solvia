![Solvia logo](assets/logo.webp)

# Solvia

> **DRAFT — PayPal AI Hackathon project, in preparation. Not yet submitted.**

**Solvia** — a simple AI helper that lets a college student pay what is due for her vehicle permit, fast, with PayPal.

## The name

Latin:

- **solvo** = "I pay, I settle what is due"
- **via** = "road" — the road a vehicle travels

Together: *pay what is due for the vehicle on the road.*

## Simple use case

Maria is a fictional student. It is her first day.

1. Maria picks her college.
2. The AI asks three questions only: **School? Car? Term?**
3. The AI shows the **exact total first**, taken from a real fee table (see `data/fees.csv` — 25 campuses scanned from mycampuspermit.com on 2026-10-18; prices that a campus does not publish are marked "not listed", never guessed).
4. Maria approves the total.
5. **PayPal Sandbox pays.** Test money only — no real money, no real student data.
6. Maria gets a receipt. Her permit is live. She can park.

No long form. No line. No price surprise at the end.

Full flow: [docs/USE-CASE.md](docs/USE-CASE.md)

## Tools used

| Part | Tool |
|---|---|
| Payments (central) | PayPal Sandbox — Orders API (create order → student approves → capture), called directly over HTTPS from the app |
| AI (central) | **Phi-4-mini** — a simple open model, run locally via **Ollama** (MIT-licensed model; structured JSON extraction of school / car / term) |
| Fee table | **AG Grid Community edition** (free, sponsor) — the 25-college fee table |
| College search | Built-in deterministic search ships by default; an **Elastic** (sponsor) provider sits behind the same config seam — ELSER did not run and is not claimed — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Term calendar | Native calendar list (built-in) — term start, permit end, renewal date. Bryntum is **not** used: it has no free edition, and its 45-day trial would expire during judging — see [docs/SPONSORS.md](docs/SPONSORS.md) |
| Hosted copy | **Render** (sponsor) — **planned, not yet deployed.** There is no hosted URL yet; judges run it locally with the steps below |
| Workflow sponsors | **APIMatic** (context document for Solvia's own API — [docs/apimatic-context.md](docs/apimatic-context.md)), **Postman** (sandbox flow collection — saved in [postman/](postman/)), **Zapier** (payment-complete webhook in the app → receipt email + ledger sheet; the live Zap is created in your own Zapier account — [docs/zapier-recipe.md](docs/zapier-recipe.md)), **KERNEL** (fee-refresh job — [tools/fee-refresh/](tools/fee-refresh/); the portable script ships, KERNEL is its production runner in the plan) |

Sponsors are used only where they do a real job in this app or its workflow. Forced fits were dropped.

### AI model choice

**AI model choice: Microsoft Phi-4-mini — a U.S. (Microsoft) open model, MIT license, 2.5 GB, runs 100% locally on the student's own computer via Ollama. Country rule for this project: no China tools, models, or services, and no Russia tools, models, or services — anywhere in the stack; only those two are out. Qwen was evaluated and not used because Qwen is a China model. Because the model runs locally, student data never leaves the student's computer and is never used to train any model.**

## Privacy — why our AI is built this way

Privacy is *why* the AI runs the way it does. The Chancellor's Office guidance says student records are FERPA-protected and tells users to never allow model training on student data — so Solvia's U.S. model runs 100% on the student's own computer, and the data never leaves it. Full explanation, with the guidance linked: [docs/PRIVACY.md](docs/PRIVACY.md)

## Rules compliance — PayPal AI Hackathon

- [x] Project **meaningfully uses both PayPal and AI** — PayPal is every money step; the AI does the asking, checking, and total.
- [x] **PayPal Sandbox only** — fictitious test accounts, mock transactions, no real money, no real student data (Maria is fictional).
- [x] **Working prototype** — judges can run it locally from the steps below, and a hosted copy is planned on Render.
- [x] **Public repository with an open-source license** — this repo is public, [MIT License](LICENSE) at the top level.
- [x] **Tools are listed and explained** — see the table above.
- [x] **New project** — new name, new repo, built during the hackathon submission period (Oct 1 – Nov 12, 2026). Structural reference only was taken from the author's earlier private permit app; this repo is permit-only and does not copy its code or secrets.
- [ ] Demo video: under 3 minutes, public on YouTube, showing the app working end to end — **to be recorded**.
- [ ] Submission text in English — this README and the Devpost text.
- [ ] Demo stays free for judges to test through the judging period (Dec 1–15, 2026).

Deadline: **Nov 12, 2026, 12:00 PM Pacific Time.**

## Run it locally

You need one thing installed first. Everything else is optional.
These steps were executed word-for-word from a fresh clone — see
[docs/CLEAN-INSTALL-LOG.md](docs/CLEAN-INSTALL-LOG.md).

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

Solvia's AI runs 100% on your own computer. To use the real model:

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
because the AI is off, and the screens work the same either way.

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
| http://localhost:5156/ | Home — pick which fictional student you are (Maria is the default) |
| http://localhost:5156/Permits/Apply | The student flow: pick your college |
| http://localhost:5156/College/Fees | The 25-college fee table (AG Grid Community) |
| http://localhost:5156/College/Dates | Term dates — term start, permit end, renewal date |
| http://localhost:5156/College/Roster | The college side: who paid, who hasn't |
| The receipt page at the end of the flow | Your permit number, plus a public check link (`/verify?t=…`) anyone can open |

### Step 6 — Try the free path (no keys, no Ollama needed)

1. On the home page, switch the student to **Priya**.
2. Open **Apply** and pick **Modesto Junior College / Columbia
   College / YCCD** — permits there are FREE.
3. Answer the 3 questions (your car in your own words, Term permit,
   Fall), review the total — **$0 — FREE** — and confirm.
4. Your permit is live. The receipt shows a check link; open it
   and it answers **VALID**.

## Why PayPal

We love PayPal — and we show it by using it, not just by saying it.

PayPal is San Jose. We are San Jose. PayPal lets a student who has never bought a permit pay in seconds, safely, with proof in her hand: a receipt and a transaction ID she can show anyone.

For this student, the payment is not a detail. The payment *is* the product. If the money step is slow, confusing, or has a surprise price, she may not park, may get a ticket, and may miss class. PayPal Sandbox let us build the whole money flow — show the total, get her approval, pay, prove it — with test money, before any real student is involved.

Things we genuinely valued while building: the Sandbox behaves like live PayPal with fictitious accounts, and the Orders flow (create → approve → capture) maps exactly onto "show price → student says yes → pay" — so our small local AI guides the student to a real, safe money step instead of just talking about paying. Honest product feedback for the PayPal team will be in the Devpost submission text.

Thank you, PayPal. Built with PayPal, in San Jose.

## Data note

`data/fees.csv` is research data scanned 2026-10-18 from the public pages of mycampuspermit.com (Parking Management Bureau), the incumbent permit store for these 25 campuses, plus one clearly labeled separate source (Delta College's official site, because its PMB page returned HTTP 502). Where a campus does not publish a price, the file says "not listed on PMB page" — we never filled a price in. See the research aggregate for counts and method.

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Jose Luis Pacheco.
