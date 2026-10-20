# Stage One implementation evidence

Updated October 20, 2026 for the US-model/planner release. Verified application/test source: [`b8bf8d3f480344e50c76e6f01f2336e96902f651`](https://github.com/jlhpacheco/solvia/tree/b8bf8d3f480344e50c76e6f01f2336e96902f651). Older commits below identify historical evidence, not the current model release.

The [official rules, section 6](https://paypalaihackathon.devpost.com/rules) describe Stage One as a pass/fail review of viability, theme fit and reasonable use of the required APIs/SDKs. Solvia has technical implementation evidence for those points. The organizers decide the result; this page does not establish personal eligibility, legal clearance or final submission.

## Current implementation and verified evidence

**Describe visits → confirm an editable schedule → compare catalog day/term costs → describe/review a vehicle → confirm a server-priced single permit → PayPal SDK checkout → verified completed capture → receipt/PDF and public verification.**

OpenAI's US-developed **`openai/gpt-oss-120b`**, hosted by Together AI, drafts only constrained fields for review. The planner uses confirmed weekdays/weeks, actual demo date windows, integer cents and server-resolved category; it cannot purchase several day passes. Daily continuation starts only the first planned visit. The model has no price or payment tools. A read-only Operations view helps inspect saved payment references and separate Paid from Active; it is not a new provider lookup or production staff authentication.

| Technical point | Current evidence / practical boundary |
|---|---|
| Actual runtime AI | **12/12** distinct live cases passed in **26.4 seconds**: eight vehicle and four schedule, all observed sources `together`. Both readers require the exact returned model ID before accepting schema-shaped/grounded final content. The suite creates no PayPal order or payment. |
| Observed response time | Eleven recorded application/browser observations: median **1.352 seconds**, range **0.815–2.646**. The uncertainty case has no elapsed observation. These are not model-only timings or a latency guarantee. |
| Backend | **517/517** passed, zero failed/skipped, in **13 seconds**. Covers current model/configuration identity, bounded responses/deadlines, planner dates/prices, payment review and the existing payment/issuance/ownership/PDF contracts. |
| Controlled browser | **132/132 passed**, zero failed/flaky/skipped/global errors: 108 main (54 desktop/54 phone) in **314.6 seconds**, plus 24 tablet in **121.3 seconds** across portrait/landscape Chromium/WebKit. Main includes 36 smoke executions; tablet has no smoke tags. TypeScript checks passed. These controlled cases are separate from the 12 actual model cases. |
| Real Sandbox payment | Original Fresno $30.00 USD term purchase: order `1G1852006N6969449`, capture `7WR24202DB5257942`, receipt `SOL-2026-197973`. Independent lookup confirmed COMPLETED/exact money; receipt/PDF matched IDs. The model change creates no new payment evidence. |
| Payment breadth and retries | Historical 20-check matrix: four campuses, 14 completed captures/$945 gross test funds, two free terms, employee/missing-price guards and 16 checked Letter PDFs. Repeat create/capture reused references; independent lookup found one capture per successful order. Original $30 excluded. |
| Failure and rendering gates | Two further real-provider negatives remained unpaid/no paid permit; two separate actual remote-SDK/no-JavaScript smokes created no order. Wallet/Personal-buyer UI approval remains unverified. |
| Sponsor / saved-record use | AG Grid Community runs in fee comparison and the saved-payment view, with native table/filter fallback. Final staff recheck summed 15 rendered saved Paid rows to $975; that copied-data snapshot includes the original $30 beyond $945 and is not a new capture total. |

See the [US-model/planner note](US-MODEL-AND-PLANNER.md), [testing instructions](TESTING.md) and [quality-control record](QUALITY-CONTROL.md). Sanitized local live evidence is `outputs/Solvia-US-model-verification.json`; the backend result is `gptoss-release-verified.trx`. US model origin does not establish US-only processing, retention settings or ZDR.

## Judge reproduction path

1. Follow [README setup](../README.md#run-it-locally), provisioning server-only Sandbox/Together credentials privately. Open the printed app address, normally `http://localhost:5156`. Use fictional adult inputs; the person switcher is a demo, not production login.
2. Select Maria, **Plan parking**, Fresno City College, and a future start inside the selected demo academic/permit window. Try `Tuesdays and Thursdays for three weeks`. Review the Together source and editable choices. Six $1 visits compare with a $30 term; this is a catalog estimate, not prepaid visits or verified campus eligibility. Choose **Start with one day pass** only when testing one day.
3. Alternatively start **Ask Solvia**, choose the college/permit/term, then enter `My fictional blue Honda Civic has license plate number 7ABC123, CA.` Review/correct every field. Verify the exact researched CSV total before payment; missing fees stay unavailable.
4. Use a separate Personal Sandbox buyer for interactive wallet testing. The independently verified automated path uses PayPal's official Sandbox test-card confirmation followed by Solvia's capture endpoint; do not claim that this establishes wallet login approval.
5. Inspect the receipt/PDF references and current public validity. Under **Payment review**, compare saved count/gross with filtered rows and read the source banner; Refresh records does not recheck PayPal. A paid historical PDF is not proof of a currently active permit.
6. Opt into [live AI checks](TESTING.md#opt-in-live-ai-checks) separately from the [Sandbox matrix](SANDBOX-MATRIX.md). The matrix makes additional test-money purchases; inspect saved references before rerunning.

Source: [Together vehicle adapter](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/TogetherExtractionService.cs), [schedule reader](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/VisitScheduleReader.cs), [deterministic planner](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/VisitPlannerService.cs), [pricing/application boundary](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/ApplicationService.cs), [checkout validation](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/CheckoutService.cs) and [live tests](https://github.com/jlhpacheco/solvia/tree/main/tests/ai).

## Historical evidence — not retroactive US-model verification

The previous Qwen reader passed eight distinct live cases twice on `e3a4993`; the historical backend 371/371 and 82 aggregate controlled cases belong to that checkpoint. Source `92cba211` later passed 390/390; 84 desktop/phone plus 16 tablet on `eb488ab1` gave 100 controlled cases across two suites. Those models, timings and SHAs are preserved rather than relabelled GPT-OSS.

The earlier 156.086-second recorded action sequence used three actual Together/Qwen calls and a separate recorded $30 capture, order `5CV7753505063622B` / capture `9AP215253S500404B`, with independent one-capture confirmation, PDF retrieval and anonymous privacy checks. Its final narrated/captioned MP4 was 156.1 seconds. That purchase is excluded from $945. The current 160-second US-model replacement names GPT-OSS 120B through Together accurately and explicitly distinguishes reused payment footage; public YouTube publication remains pending. Printed demo permits grant no actual parking rights.

## Final submission remains open

The last verified Devpost status was **Draft, 2 of 5 steps**, video URL blank. Public YouTube publication under three minutes, clean full-path/free judge-access verification, personal answers and participant eligibility/rights confirmations remain open. Eligibility checkboxes 16–18 and final terms remain unchecked; no final submission is recorded. The current US-model story/judge fields and Solvia-US-model-QA.zip attachment were saved and read back; see RELEASE-2026-10-20.md. Maintain working access through December 15, 8 a.m. Pacific. Consult the [full rules audit](HACKATHON-RULES-AUDIT.md).
