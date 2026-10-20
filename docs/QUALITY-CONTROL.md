# Solvia quality control process

**Working draft - October 19, 2026**  
**Scope:** our fictional campus-permit demonstration and PayPal Sandbox integration.  
**Release owner:** Jose Luis Pacheco.  
**Tested application source:** `aa6781a1d17b42c778040bc1f834045977681bc4`.

## How we decide what to test

We prioritize failures that could charge the wrong amount, falsely issue a
permit, change an earlier permit's identity, lose a license plate, or mislead
someone about an AI answer. We then test navigation, accessibility, responsive
layouts and recovery paths. This record describes work performed by our
project team with agent assistance; the human creator remains accountable for
the code, claims and release decision.

Our acceptance rules are concrete:

| Invariant | Evidence we require |
|---|---|
| The catalog controls price | The selected campus, permit and category produce the published CSV fee. Missing prices block checkout. AI output and browser input cannot supply a fee. |
| An order is not proof of payment | Only a completed capture with the expected gross amount, USD currency and capture ID can activate a paid permit. |
| Retries do not duplicate payment | Repeated creation reuses the order; repeated capture returns the same durable capture and receipt. Provider lookup shows one completed capture. |
| Rejected payments cannot issue permits | Unapproved and declined cases remain unpaid and cannot download a live permit PDF or obtain a live verification link. |
| People can correct their vehicle | We preserve the plate and chosen college/permit, provide editable review fields, and validate submitted details before saving. |
| AI status is visible | We label model versus rules output and preserve human review. We describe the configured endpoint accurately. |
| Records remain usable | Receipts and PDFs carry matching identifiers; public verification works without the buyer cookie and avoids disclosing the full plate. |

CSV pricing is our agreed hackathon scope. The catalog is a dated research
fixture, not a live college feed or an institutional authorization.

## Our change and verification workflow

1. **Reproduce.** We capture the failing URL, inputs, expected result and actual
   result using fictional data. We identify whether the failure belongs to
   application logic, a provider, configuration, or the test harness.
2. **Fix the cause.** We change the smallest responsible boundary. Examples
   include missing service registration, numeric plate extraction, exact-money
   checks, and retaining a useful checkout error instead of issuing a receipt.
3. **Add a regression.** We choose an assertion that would fail for the original
   bug: no model invocation for oversized input, no saved invalid vehicle, or
   no permit after a declined capture. We include a valid boundary case where
   it helps show the rejection is intentional.
4. **Verify in layers.** We run focused checks first, then the relevant broader
   suite. We use actual Sandbox requests when the question concerns provider
   behavior. We inspect rendered pages and selected PDFs as well as text.
5. **Review and record.** Agents help inspect separate areas, challenge claims
   and review evidence. We reconcile their findings, inspect the final diff,
   record the tested commit and limitations, then commit and push authorized
   changes. We verify the remote commit rather than assuming a push succeeded.

A provider timeout or interrupted test triggers a status lookup before another
purchase. We retain order/capture references so a failed assertion after
payment does not encourage a blind second charge.

## What our current evidence demonstrates

| Layer | Recorded result | Scope |
|---|---|---|
| Backend | 367 C#/xUnit checks passed, zero failures and none skipped | The final coordinated suite supersedes the earlier 244- and 308-test baselines. It includes pricing, ownership, payment validation/retries, routes/DI/Razor, antiforgery, PDFs, AI input/vehicle guards and offline Meta validation/selection. |
| Browser | 82 distinct TypeScript/Playwright checks passed across recorded runs | The earlier 80 combined a full run and nine corrected reruns. Two new loading-feedback checks passed after harness corrections; the 46-check main journey subset passed again. A final clean 30-check smoke run passed in 2.6 minutes. Those smokes are included in the 82. |
| Multi-college matrix | 20 checks passed | 16 Student/Public day/term cases across four campuses, plus two Employee exemptions and two unpublished-price guards. |
| Negative provider cases | Two additional checks passed | Actual unapproved-order and official refused-card responses; no paid permit issued. |
| Native payment options | Two separate smoke checks passed in 33.5 seconds | Actual remote SDK options and a JavaScript-disabled continuation; no PayPal order or capture created. |
| Permit documents | All 16 matrix PDFs passed content/Letter-page checks | Exact campus, plate, category, type, fee, receipt and provider references; three representative renders also inspected visually. |

The original matrix and negative cases total **22 Sandbox integration checks**.
The two native-option smoke checks are separate. The matrix produced **14
successful captures totaling $945.00 USD gross Sandbox test funds** and two
free term permits. The earlier standalone $30 Fresno capture is separate and
excluded from that matrix total. No real money was charged.

For paid cases, we independently queried PayPal before and after capture. We
matched the expected Sandbox seller, order amount/currency, completed nested
capture, and durable app references. We compare the gross capture with the
permit fee; a merchant's net balance after provider fees is a different value.
Create/capture retries reused references and left one successful capture per
paid matrix order. All demo campuses use one Solvia Sandbox seller.

The unapproved case returned HTTP 409 with `ORDER_NOT_APPROVED` and no capture.
The refused-card case returned a real `DECLINED` capture with processor code
`0500`; Solvia reported `completed: false`. Both owned PDF requests returned
HTTP 400. The refused case passed in a targeted rerun after we corrected a
test assertion to allow PayPal's optional amount breakdown while checking the
exact $1.00/USD total. This added no successful payment to the matrix.

## Controlled tests and service boundaries

We deliberately simulate SDK failures, cancellations, pending responses and
blocked scripts in isolated browser tests. Those checks validate our recovery
behavior; real-provider capture evidence comes from the separate Sandbox
runs. The native smoke loaded PayPal and Debit or Credit Card options from the
actual remote SDK, and checked one protected continuation with JavaScript
disabled. Rendering those options does not establish buyer login or approval.

Our tests use fictional people, unique fake plates and published Sandbox test
cards. We keep client secrets and bearer tokens in private configuration or
process memory, exclude databases/raw evidence from Git, disable provider
traces, and redact transport errors that could contain request headers. We
review screenshots, logs and public artifacts before sharing. The demo person
switcher exercises ownership scenarios; it is not production authentication.

## Current release gates and known limits

- **AI application path:** a direct warmed CPU Phi-4-mini request answered in
  32.26 seconds. The app's 10-second budget falls back before that response.
  The optional hosted Meta Standard Muse Spark 1.3 adapter is implemented and
  tested offline. Its private key and live application verification remain
  pending; no hosted API calls have been made. Standard mode excludes training
  but does not establish zero retention or US-only processing. Use fictional
  adult (18+) demo details. We retain the visible fallback and editable review.
- **Hardening release check:** the final 367-test backend run passed in 34
  seconds. It includes plate grounding/uncertainty, 1,000-character limits
  before inference, 50-character vehicle fields, named state/color and exact
  term/type vocabulary, referenced vehicle-state protection and provider
  selection without network calls. TypeScript checking passed. An earlier
  coordinated run passed 331/332: a new case exposed `might` being read as a
  plate. We fixed it and added uncertainty regressions. Independent review
  also caught substring vocabulary acceptance. Earlier test-host and browser
  harness failures remain retained, with successful reruns recorded separately.
- **External coverage:** Personal-buyer wallet approval, delivered verified
  webhooks, refunds and live-money processing remain unverified. Safari and
  physical-device testing are not covered by Chromium mobile viewports.
- **Submission readiness:** clean judge access with working AI/payment setup,
  first-time user feedback and the public demonstration video remain separate
  work. We make no campus policy, security certification or compliance claim.

## Evidence index

We keep reproducible instructions and dated evidence in the public repository:

- [Setup and prerequisites](https://github.com/jlhpacheco/solvia/blob/main/README.md)
- [Backend/browser test instructions](https://github.com/jlhpacheco/solvia/blob/main/docs/TESTING.md)
- [Opt-in Sandbox suites and boundaries](https://github.com/jlhpacheco/solvia/blob/main/docs/SANDBOX-MATRIX.md)
- [Dated payment results and references](https://github.com/jlhpacheco/solvia/blob/main/docs/SANDBOX-RESULTS-2026-10-19.md)
- [Reusable TypeScript Sandbox tests](https://github.com/jlhpacheco/solvia/tree/main/tests/sandbox)
- [Rules/readiness audit](https://github.com/jlhpacheco/solvia/blob/main/docs/HACKATHON-RULES-AUDIT.md)
- [Project license](https://github.com/jlhpacheco/solvia/blob/main/LICENSE) and [third-party notices](https://github.com/jlhpacheco/solvia/blob/main/NOTICES.md)

## Change and retest log

For each material change, append an entry with failures and corrections and
retain the tested source version with the evidence. A pass applies to that
version and scope. This working log is the starting record for that process.

| Record field | Current hardening entry |
|---|---|
| Date | October 19, 2026 |
| Tested source | `aa6781a1d17b42c778040bc1f834045977681bc4` (application code; documentation follows separately) |
| Change / risk | AI input, plate grounding, enum and vehicle-history guards; optional hosted Meta reader. |
| Observed checks | Backend 367/367 passed, zero skipped; TypeScript checking passed; 82 distinct browser checks across retained runs, including final 30/30 smoke checks in 2.6 minutes. |
| Evidence / remaining gate | Local `meta-release-verified.trx` and separate browser HTML reports retained. Live hosted AI, wallet approval, judge access and video remain open. |
| Review | Codex agent review; release-owner approval pending. |

Copy these fields for the next material change, including the exact tested SHA,
command, observed passes/failures, evidence link and remaining gate.
