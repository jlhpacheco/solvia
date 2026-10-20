## Inspiration

Getting to class should not require deciphering a parking portal. A student or visitor needs three clear answers: which permit fits, what it costs, and whether payment really completed. Solvia brings those steps into one mobile-friendly journey, built in San Jose.

The operational stakes are concrete. San Jose–Evergreen Community College District's [FY2025–26 adopted budget, p.47](https://sjeccd.edu/sites/default/files/documents/FY2025-26_Adopted_Budget_09-09-25.pdf) budgets a $1,995,985 transfer from Fund 10 into Parking Fund 11, alongside $100,000 in parking fees. This combined parking/transportation fund includes staffing and operations; the transfer is neither a software bill nor savings Solvia has achieved. Monterey Peninsula College documents parking-fund support for campus safety and repairs, while Bakersfield College's January 2026 budget agenda identifies parking operating deficits. These examples motivate measuring administrative effort, corrections and cost per permit. They do not establish a universal classroom subsidy or a proven benefit from our app. [Sources, distinctions and proposed pilot](https://github.com/jlhpacheco/solvia/blob/main/docs/IMPACT-AND-DIFFERENTIATION.md).

Published MyCampusPermit instructions already support online purchase, receipts and printable permits; CSU Stanislaus documented its use by 2018. Our differentiation is the focused workflow: a reviewable language draft, consistent campus comparison, deterministic pricing and verified payment references in the resulting permit. We are demonstrating that combination, without claiming to have invented its individual parts or measured superiority over a current campus deployment.

## What it does

Describe your car, review the details, see the published fee, pay with PayPal, and download a verifiable campus permit.

- Choose from a research catalog of 10 California colleges. AG Grid Community provides searchable, sortable campus-fee comparisons.
- Enter a vehicle in one sentence. The configured AI reader drafts the plate, make, model, color, and state; the person reviews and edits every field before continuing. A clearly labeled rules fallback keeps the demo usable when the model is unavailable.
- Students and public visitors can choose day or term permits. The employee demo illustrates an exemption. These are fictional personas and demonstration policies, not actual campus authorization.
- Review the exact server-calculated USD total before checkout. Prices come from the checked-in CSV catalog; the model cannot invent or change a fee. Missing prices block payment.
- Pay through PayPal Sandbox. Solvia issues a paid permit only after a completed capture matches its saved order, amount, and currency. The receipt and printable PDF include the PayPal order ID and capture ID, and a verification link checks the permit's saved status.

## How we built it

Solvia uses ASP.NET Core MVC on .NET 10, Entity Framework Core, and SQLite. Shared application and pricing services support both the conversational and conventional form paths. PayPal Orders REST APIs create and capture payments; the JavaScript SDK supplies checkout buttons. Repeated create/capture requests reuse saved references, and recovery checks provider state before declaring a permit paid.

The default AI reader uses Together AI with `Qwen/Qwen3.5-9B`, called from the server with a private API key. It returns constrained JSON with reasoning disabled, temperature 0 and a 512-output-token cap. Shared validation preserves grounded vehicle details and rejects unusable responses; people review and edit the draft before saving. College and permit choices stay in the UI, and the model has no price field or payment tools. Microsoft Phi-4-mini via Ollama remains optional; the built-in rules reader is clearly labeled when a model cannot answer.

The chat and form readers share admission limits before inference: 20 requests per client and 60 globally per minute, with two concurrent requests per client and four globally. Rejected requests receive 429/Retry-After, and the form keeps the person's answers. These limits are process-local and reset on restart; they are not authentication, a distributed quota or a provider billing cap.

We chose a small model for this focused extraction task. At Together's published rates, an illustrative 1,000-input/200-output-token request costs about $0.00022; this is a token-cost estimate, not measured spending or a fixed permit cost. The final eight-case run observed a median 2.4 seconds from submit to review, range 1.6–5.3 seconds on this computer. Those measurements include the application flow and are not a latency guarantee. Together processes the hosted inputs; account privacy settings, zero retention and US-only residency have not been verified. The demo uses fictional adult personas and vehicles.

AG Grid Community does real work comparing campus fees, with a native table fallback when its CDN is unavailable. PDFsharp generates printable permits; dependency and font licenses are documented in the repository. CSV pricing is intentionally sufficient for this hackathon prototype; no live college pricing feed is claimed.

## Challenges we ran into

Numeric license plates and labels such as "license plate number" initially failed extraction. We fixed those cases and added regressions. The first Together backend run passed 366/367: delayed timeout cancellation under load exposed a late-response bug. We added measured-deadline checks and four deterministic regressions, then passed 371/371. We also repaired service registration, navigation, fee-grid rendering, payment-error recovery, and references in receipts/PDFs. A successful return page alone must never be mistaken for captured money.

## Accomplishments we're proud of

On October 19, 2026, a real PayPal Sandbox test-card transaction for a $30 Fresno City College term permit completed through Solvia's REST capture endpoint. An independent PayPal order lookup confirmed the completed capture and exact USD amount. The paid receipt and PDF contain matching order and capture IDs. This verifies the test-card API path; Personal Sandbox wallet approval remains unverified.

The final backend-tested source `92cba211` passed 390/390 tests in 12 seconds with zero failures or skips. The complete desktop/phone TypeScript run passed 84/84 checks in 4.0 minutes, including 32 smoke checks. That browser run preceded the last receipt-view validity change; five rendered backend cases cover its paid/free, active, denied, future and expired states. Payment confirmation is preserved without calling an inactive permit live. A separate 16/16 tablet run passed in 2.3 minutes on `eb488ab1`, covering Chromium/WebKit portrait and landscape. The two browser suites total 100 distinct controlled checks, excluding live AI.

Earlier checkpoints remain in the record: source `e3a4993` passed 371 backend tests and a fresh 30/30 smoke run within 82 cumulative controlled browser checks. Separately, eight distinct real Together application cases passed twice on that model-tested source, including the rerun after credential replacement and deadline hardening. Every case returned `source=together`; these are eight distinct live cases, not 16, and they are separate from the controlled browser suite. No new eight-case live rerun on `92cba211` is claimed.

The separate PayPal integration matrix passed 20 checks across four colleges: student/public day and term permits, employee exemptions and blocked unpublished prices. Fourteen captures completed for $945 gross Sandbox test funds; the original $30 test is separate. Create/capture retries reused references, and independent provider lookups found one capture per successful order. All 16 student/public PDFs passed content validation. Two further real-provider checks verified unapproved-order/refused-card handling, while two actual SDK/no-JavaScript checks verified payment options and protected continuation without creating orders. Our quality-control document records failures, corrections, evidence and remaining gates.

An additional recording run captured a separate $30 Sandbox purchase: order `5CV7753505063622B`, capture `9AP215253S500404B`. Independent lookup and retries found one completed capture; paid PDF download, later retrieval and anonymous verification privacy were exercised. The captured WebM is 167.120 seconds, including an 11.034-second post-action closing tail. Its 156.086-second action sequence includes three actual Together calls. The final captioned MP4 is 156.1 seconds, with natural synthetic narration at unchanged tempo. Public YouTube publication remains pending. This run uses a separate demo database and is excluded from the historical $945 matrix total; its three AI calls do not add new distinct eight-case-suite scenarios.

## What we learned

A simpler front end requires careful boundaries behind it: AI drafts the car details, the catalog supplies the price, the person confirms the information, and PayPal supplies payment truth. Showing those boundaries makes the experience easier to trust and debug.

## What's next for Solvia

Measure first-time mobile usability, verify clean full-path judge access and Personal-buyer wallet approval, and publish the finished 156.1-second narrated demo publicly on YouTube. Live webhooks, refunds, actual Safari and physical-device testing remain unverified; Playwright WebKit coverage is not a physical Safari-device check. Production use would require real authentication, campus-approved eligibility/pricing and operational/privacy review. Successful inference does not establish a provider privacy setting or institutional approval.

## Hackathon progress and current status

Solvia is an independent application first built during this hackathon. Its development includes PayPal payment verification and recovery, vehicle extraction and review, printable permits, fee comparison, responsive styling, automated browser tests, and documentation. The repository records iterations made within the event. This entry is a working Sandbox prototype using fictional people, not a live campus payment service.

Source and setup: https://github.com/jlhpacheco/solvia
