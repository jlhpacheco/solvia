## Inspiration

A student or visitor should be able to answer three questions quickly: which parking option fits my visits, what will it cost, and where is my proof after paying? Solvia connects those decisions in one responsive campus-permit journey.

The operational stakes are concrete. San Jose–Evergreen Community College District's [FY2025–26 adopted budget, p.47](https://sjeccd.edu/sites/default/files/documents/FY2025-26_Adopted_Budget_09-09-25.pdf) budgets a $1,995,985 transfer from Fund 10 to Parking Fund 11, alongside $100,000 in parking fees. That combined parking/transportation fund includes staffing and operations; the transfer is neither a software bill nor savings Solvia has achieved. Monterey Peninsula College documents parking-fund support for campus safety and repairs, and a January 2026 Bakersfield College budget agenda identifies parking operating deficits. These examples motivate measuring administrative work and cost per permit. [Sources and proposed pilot](https://github.com/jlhpacheco/solvia/blob/main/docs/IMPACT-AND-DIFFERENTIATION.md).

MyCampusPermit already supports online purchases, receipts and printable permits; published instructions document its use by 2018. Solvia's distinction is the connected experience: a reviewable schedule-to-cost comparison, editable language-assisted vehicle entry, catalog-controlled totals, verified capture references and retrievable proof. We are demonstrating that combination, without claiming to have invented its individual parts or measured superiority over a current campus deployment.

## What it does

Plan your visits, confirm your vehicle, review the exact total, pay through PayPal, and retrieve a verifiable printable permit.

- **Plan before paying.** Describe weekdays and weeks, then confirm the editable schedule. A Fresno example compares six visits at $1 each with a $30 term permit. Continuing with daily parking starts **one $1 pass for the first visit**, not six prepaid visits. Missing prices stay unavailable; comparison creates no order.
- **Review the AI draft.** OpenAI's **US-developed GPT-OSS 120B, served through Together AI**, drafts vehicle or schedule fields. People can correct them before continuing. A clearly labeled rules fallback keeps the demo usable when the model cannot answer.
- **Choose the right demonstration path.** Students and public visitors may buy day or term permits. An employee persona demonstrates an exemption. These are fictional adults and demo policies, not campus-approved eligibility or parking authorization.
- **See a server-calculated fee.** A dated CSV research catalog covers 10 California colleges. The AI has no price field or payment tools. Unpublished fees block checkout; there is no claimed live college feed.
- **Pay and receive proof.** PayPal's official SDK provides branded checkout options. Solvia issues a paid permit only after its backend verifies a completed capture, saved order, exact USD amount and capture ID. Receipt and PDF include matching PayPal order/capture references. My permits supports later download; public verification avoids exposing the plate.
- **Review saved operations.** AG Grid Community powers fee comparison and a demo-only staff view of saved application records. Native filters/tables remain usable without JavaScript or the CDN. Counts and gross amounts concern saved Sandbox records, not real college revenue or a new PayPal settlement lookup.

## How we built it

ASP.NET Core MVC on .NET 10, Entity Framework Core and SQLite support shared application, pricing and issuance services. The planning path passes a confirmed first-visit date into the same review/application flow. Server-side category resolution and exact-money rules control the price.

PayPal Orders REST APIs create and capture payments. Repeated requests reuse saved references, and recovery checks provider state before declaring payment complete. A return page or an order ID alone cannot issue a paid permit. Payment confirmation is separate from current permit validity: expired/future permits retain payment history and PDF access without a misleading live badge.

Both Together readers pin `openai/gpt-oss-120b`, use constrained JSON, low reasoning effort and a 2,048-token total completion cap. Returned model identity, schema, grounding, response size, cancellation and elapsed deadlines are checked before a Together source label is accepted. Only final content is read; model reasoning is not shown or logged. There is no fine-tuning. Optional Microsoft Phi-4-mini via Ollama and the deterministic fallback remain separately labeled.

Vehicle, chat and schedule reads share process-local admission limits: 20 requests per client and 60 globally per minute, with two concurrent per client and four globally. A rejected request receives 429/Retry-After before inference and preserves form answers. These are not authentication, distributed quotas or a provider billing cap.

OpenAI supplies the US-developed model; Together hosts inference using the participant's existing private API key. **US model origin does not establish US-only processing or data residency.** Account privacy settings and zero retention have not been verified. Use fictional inputs without personal information. [Model, cost and architecture details](https://github.com/jlhpacheco/solvia/blob/main/docs/US-MODEL-AND-PLANNER.md).

PDFsharp 6.2.3 generates US Letter permits with embedded Roboto fonts. The repository preserves pinned dependency/font notices and the Solvia MIT license. AG Grid Community is the demonstrated additional sponsor integration; Enterprise/Studio, APIMatic execution and hosted Render deployment are not claimed.

## Challenges and quality control

Numeric plates and “license plate number” labels originally failed extraction. Regressions now cover those cases, uncertainty, invented facts and instructions that try to change the selected campus or fee. A delayed cancellation timer exposed late-response acceptance; monotonic deadline guards now reject late answers.

Three agents reviewed actual Student, Public visitor and staff tasks. These were **AI-assisted automated usability reviews, not human study participants**. Their findings led to clearer inactive-permit labels, Not priced versus genuinely free amounts, removal of an empty planner error and mobile focus on comparison results. Final read-only rechecks confirmed these fixes. A separate study kit is ready for future human recruitment; no human findings, campus adoption or ROI are claimed.

The replacement-model backend suite passed **517/517 tests**, zero failed or skipped, in 13 seconds. A separate live TypeScript Playwright suite passed **12/12 GPT-OSS application cases**: eight vehicle cases and four schedule cases. Every observed source was Together, and the adapters required the returned model identity to match. The 11 timed submit-to-review observations had a median 1.352 seconds, range 0.815–2.646 seconds on this computer; these include UI/navigation time and are not a service guarantee. Older Qwen results are historical and are not relabeled as GPT-OSS evidence.

The final controlled TypeScript Playwright runs passed **108/108 desktop/phone checks**, including **36 smoke executions**, in 5.2 minutes, plus **24/24 Chromium/WebKit tablet checks** in 2.0 minutes. That is **132 controlled browser checks**, separate from live AI. TypeScript typechecking passed, including the tablet tests. These checks exercise recovery, no-JavaScript paths, editable review, planner handoff, payment references and responsive layouts. A duplicate simulated-SDK failure was traced to the harness loading the adapter twice; the corrected harness waits for the original deferred script and asserts a single render. Emulated WebKit coverage is not a physical iPad or Safari-device claim. [Versioned test report](https://github.com/jlhpacheco/solvia/blob/main/docs/TESTING.md). Initial failures and corrections are retained; reruns do not add new distinct cases.

The independent PayPal integration matrix passed 20 checks across four campuses: Student/Public day and term choices, Employee exemptions and unpublished-price guards. Fourteen captures completed for **$945 gross Sandbox test funds**; two term permits issued free. Create/capture retries reused references and independent provider lookups found one capture per paid order. All 16 Student/Public PDFs passed content/Letter-page validation. Two further actual-provider checks exercised unapproved/refused-card outcomes, and two actual SDK/no-JavaScript smokes created no orders. These are separate from controlled browser tests and live AI.

A separate recorded $30 term purchase has order `5CV7753505063622B` and capture `9AP215253S500404B`, with independent verification, matching paid PDF and later retrieval. It is excluded from the $945 matrix. The replacement video uses freshly recorded GPT-OSS inference; any reused payment footage is explicitly this **separate term purchase**, not the planner's $1 first-day example. The verified payment method uses official Sandbox test-card approval through REST, then Solvia's capture endpoint; interactive Personal-buyer wallet approval remains unverified.

[Quality-control process and evidence](https://github.com/jlhpacheco/solvia/blob/main/docs/QUALITY-CONTROL.md).

## What we learned

Simple screens depend on clear authority behind them: the model drafts, the person confirms, the catalog prices, and PayPal confirms payment. A planner estimate, captured money and currently valid parking proof are different facts; the product should make each understandable.

## What's next

Publish the replacement narrated demo publicly on YouTube, verify clean full-path free judge access and Personal-buyer wallet approval, and run the prepared study with real adult volunteers. Live webhook delivery, refunds and physical-device behavior remain unverified. Production requires authentication, campus-approved rules, institutional agreements and operational/privacy review.

Solvia is an independent application first built during this hackathon. It is a working Sandbox prototype with fictional adults, not a live campus payment service. Source and setup: https://github.com/jlhpacheco/solvia
