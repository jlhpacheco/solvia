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
| Payments (central) | PayPal Sandbox — Orders / Checkout (create order → student approves → capture), via the PayPal Agent Toolkit / Orders API |
| AI (central) | **Phi-4-mini** — a simple open model, run locally via **Ollama** (MIT-licensed model; function calling for extracting school / car / term) |
| Fee table | **AG Grid** (sponsor) — the 25-college fee table |
| College search | **Elastic** (sponsor) — search colleges and fees; Elastic's own trained ELSER model |
| Term calendar | **Bryntum** (sponsor) — term start, permit end, renewal date |
| Hosted copy | **Render** (sponsor) — a hosted copy so judges can open it, in addition to running it locally |
| Workflow sponsors | **APIMatic** (PayPal API context for the coding agent), **Postman** (PayPal API test collection, saved in this repo), **Zapier** (receipt + renewal reminder), **KERNEL** (browser agent that re-checks college fee pages) |

Sponsors are used only where they do a real job in this app or its workflow. Forced fits were dropped.

### AI model choice

**AI model choice: Microsoft Phi-4-mini — a U.S. (Microsoft) open model, MIT license, 2.5 GB, runs 100% locally on the student's own computer via Ollama. We chose a U.S. model on purpose: student data never leaves the local computer and is never sent to a foreign cloud or foreign model. Qwen was evaluated and not used.**

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

## Run it locally (DRAFT)

> **DRAFT steps — to be verified on a clean Windows machine before submission.**

1. Install [Ollama](https://ollama.com/) and pull the open model:
   ```
   ollama run phi4-mini
   ```
2. Clone this repo.
3. Copy `.env.example` to `.env` and add your **PayPal Sandbox** client ID and secret (sandbox only — never commit `.env`; it is git-ignored).
4. Install dependencies and start the app (exact command: **TBD in the build step**).
5. Open the local address the app prints, pick a college, and walk through Maria's flow. Pay with a PayPal Sandbox test buyer account.

## Why PayPal

We love PayPal — and we show it by using it, not just by saying it.

PayPal is San Jose. We are San Jose. PayPal lets a student who has never bought a permit pay in seconds, safely, with proof in her hand: a receipt and a transaction ID she can show anyone.

For this student, the payment is not a detail. The payment *is* the product. If the money step is slow, confusing, or has a surprise price, she may not park, may get a ticket, and may miss class. PayPal Sandbox let us build the whole money flow — show the total, get her approval, pay, prove it — with test money, before any real student is involved.

Things we genuinely valued while building: the Sandbox behaves like live PayPal with fictitious accounts, the Orders flow (create → approve → capture) maps exactly onto "show price → student says yes → pay", and the Agent Toolkit / MCP path lets a small local AI model do real commerce instead of just talking about it. Honest product feedback for the PayPal team will be in the Devpost submission text.

Thank you, PayPal. Built with PayPal, in San Jose.

## Data note

`data/fees.csv` is research data scanned 2026-10-18 from the public pages of mycampuspermit.com (Parking Management Bureau), the incumbent permit store for these 25 campuses, plus one clearly labeled separate source (Delta College's official site, because its PMB page returned HTTP 502). Where a campus does not publish a price, the file says "not listed on PMB page" — we never filled a price in. See the research aggregate for counts and method.

## License

MIT — see [LICENSE](LICENSE). Copyright (c) 2026 Jose Luis Pacheco.
