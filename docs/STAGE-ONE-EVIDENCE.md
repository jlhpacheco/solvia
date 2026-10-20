# Stage One implementation evidence

Updated October 20, 2026. Final backend-tested application source:
`92cba2119af4c7d5be7b7b51e70c99f9f2aecb8e`.
The earlier live-model-tested source is identified separately below.

The [official rules, section 6](https://paypalaihackathon.devpost.com/rules)
describe Stage One as a pass/fail check of baseline viability, theme fit and
reasonable use of the required APIs/SDKs. Solvia has working implementation
evidence for those technical points. The organizers decide the result;
this page does not establish eligibility or final submission.

## Why PayPal and AI belong in the same flow

**Describe a vehicle → review/edit its details → confirm the server-priced
permit → PayPal SDK checkout → verified COMPLETED capture → receipt/PDF and
public verification.**

Together AI drafts vehicle fields from a sentence, including numeric plates;
the person confirms the result. College, permit type and term remain UI
choices. The model has no price field or payment tools. The server snapshots
the researched CSV fee, creates/captures the PayPal order and issues a paid
permit only after checking capture status, amount, currency and ID. Payment
verification therefore controls the product's main outcome.

## Verified evidence

| Technical point | Observed result |
|---|---|
| Real runtime AI | Eight distinct Together `Qwen/Qwen3.5-9B` application cases passed twice on `e3a4993`, including the rerun after credential replacement/deadline hardening. Every source was `together`; controlled fallback is not counted as live AI. No new eight-case rerun on `92cba211` is claimed. |
| Real Sandbox payment | Original Fresno term purchase: $30.00 USD; order `1G1852006N6969449`, capture `7WR24202DB5257942`, receipt `SOL-2026-197973`. Independent PayPal lookup confirmed COMPLETED and exact money; receipt/PDF matched both IDs. |
| Payment breadth and retries | Separate 20-check matrix covered four campuses, Student/Public day/term choices, exemptions and missing-price guards. Fourteen completed captures totaled $945 gross test funds; two term permits were free. Repeated create/capture reused references; provider lookups found one completed capture per paid order. The original $30 is excluded from $945. |
| Failure and document gates | Two further real-provider checks verified unapproved/declined outcomes without paid permits. All 16 matrix PDFs passed content/Letter-page checks. Two separate actual SDK/no-JavaScript smokes created no orders. |
| Regression evidence | Final backend 390/390 passed in 12 seconds, zero failed/skipped. Complete desktop/phone TypeScript run 84/84 passed in 4.0 minutes, including 32 smokes. The browser run preceded the final receipt-view validity delta; five rendered backend cases cover it. Historical 371 backend / 82 browser / 30-smoke checkpoints remain recorded. A separate tablet run passed 16/16 in 2.3 minutes on `eb488ab1` (Chromium/WebKit portrait and landscape), bringing the controlled total to 100 across two suites. |
| Bounded public inference | Chat and form share limits of 20 requests/client/minute, 60 globally, two in flight/client and four globally; 429/Retry-After precedes inference. Limits are process-local, reset on restart and are not authentication or a provider billing cap. |
| Payment versus current validity | Stale cancel/unknown return handling preserves payment truth. Receipt headings use the shared validity rule: paid denied/future/expired permits retain payment confirmation without a live-permit claim. |
| Optional sponsor use | AG Grid Community provides actual searchable/sortable campus-fee comparison; it is additional to the central PayPal/AI flow. |

The live AI suite stops before creating a payment order. Payment/issuance is
covered by separate real-provider tests. A later 156.086-second recorded action sequence
exercised three actual Together calls and a separate $30 capture (order
`5CV7753505063622B`, capture `9AP215253S500404B`), independently checked for one
capture, plus PDF retrieval and anonymous privacy checks. It uses a separate
demo database and is excluded from the $945 matrix total. The final narrated/captioned MP4 is 156.1 seconds. Public YouTube publication
remains outstanding. All people are
fictional and payments use Sandbox funds; printed demo permits grant no
actual parking rights.

## Reproduce and inspect

1. Follow [README setup](../README.md#run-it-locally), including privately
   provisioned server Sandbox/Together credentials. Open the app at its
   printed address (normally `http://localhost:5156`). Use fictional inputs.
2. Choose Maria, **Ask Solvia**, CSU Monterey Bay, Term/Fall inside the fixture's
   validity window. Try: `My fictional blue Honda Civic has license plate
   number 7ABC123, CA.` Confirm the visible Together label and editable fields.
   Use a new fictional plate when creating another application.
3. Confirm the catalog total of $229.50 before payment. Use PayPal Sandbox
   approval, then inspect the saved capture/receipt IDs, PDF and public check.
   Interactive Personal-buyer wallet approval remains unverified; a separate
   Personal Sandbox buyer is needed. The verified automated payment path uses
   PayPal's official test-card confirmation and Solvia's capture endpoint.
4. Reproduce the [eight live AI checks and isolated regressions](TESTING.md),
   or opt into the [real Sandbox matrix](SANDBOX-MATRIX.md). The matrix creates
   additional test-money purchases; inspect saved references before rerunning.

Source: [Together adapter](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/TogetherExtractionService.cs),
[application/pricing boundary](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/ApplicationService.cs),
[checkout validation](https://github.com/jlhpacheco/solvia/blob/main/src/Solvia/Services/CheckoutService.cs),
and [live AI tests](https://github.com/jlhpacheco/solvia/blob/main/tests/ai/model-live.spec.ts).
See [dated payment results](SANDBOX-RESULTS-2026-10-19.md) and
[quality-control process](QUALITY-CONTROL.md) for evidence and limitations.

## Final submission remains open

The last verified Devpost status was **Draft, 2 of 5 steps**. The public
YouTube video under three minutes, clean full-path/free judge-access
verification, personal answers and eligibility/rights confirmations remain
open. Eligibility checkboxes have not been answered by the assistant. Local
instructions are available; no hosted Solvia deployment is claimed. Keep
working access available through the judging period. Consult the
[full rules audit](HACKATHON-RULES-AUDIT.md) before final submission.
