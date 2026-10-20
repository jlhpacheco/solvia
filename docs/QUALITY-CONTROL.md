# Solvia quality control process

**Working draft - updated October 20, 2026**

**Scope:** our fictional campus-permit demonstration and PayPal Sandbox integration.

**Release owner:** Solvia project maintainer.

**Final backend-tested application source:** `92cba2119af4c7d5be7b7b51e70c99f9f2aecb8e`.

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
| Paid does not automatically mean currently valid | Receipt headings use the shared date/status validity rule. Stale cancel returns do not contradict a saved capture, and unknown return tokens do not prove that no charge occurred. |
| Public extraction is bounded | Both extraction routes share request and in-flight quotas before calling a model; rejected requests receive 429/Retry-After and MVC answers remain available. |
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
| Backend | 390/390 C#/xUnit checks passed in 12 seconds, zero failed/skipped | Final source `92cba211`; includes AI quotas/concurrency, stale cancel messaging and five rendered paid/free validity cases in addition to the prior contracts. Historical 371/382 runs remain separate. |
| Browser | Full 84/84 desktop/phone TypeScript checks passed in 4.0 minutes, including 32 smokes; separate tablet 16/16 passed in 2.3 minutes | 100 distinct controlled checks across two suites, excluding live AI. Desktop/phone preceded the final receipt-view validity delta, covered by rendered backend cases. Tablet rerun on `eb488ab1` covers Chromium/WebKit portrait/landscape. |
| Live AI | Eight distinct Together application cases passed twice | Final same-eight rerun on `e3a4993` followed key replacement/deadline hardening; every source was `together`. Separate from the 84 controlled browser checks; no new live suite on `92cba211` is claimed. |
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
cards. We configure credentials server-side, exclude databases/raw evidence
from Git, disable provider
traces, and redact transport errors that could contain request headers. We
review screenshots, logs and public artifacts before sharing. The demo person
switcher exercises ownership scenarios; it is not production authentication.

A setup inspection accidentally exposed a hosted API key. We revoked it,
saved a replacement privately and repeated the same eight live cases
successfully. This incident and correction are part of our record; repeating
the suite does not create 16 distinct cases.

## Current release gates and known limits

- **AI application path:** the default is Together AI with `Qwen/Qwen3.5-9B`,
  constrained JSON, disabled reasoning, temperature 0, a 512-output-token cap
  and shared validation. The final live eight-case run observed median 2.4
  seconds from submit to review, range 1.6–5.3 seconds on this computer. These
  are application observations, not model-only latency or a guarantee. People
  review editable vehicle fields; the catalog supplies fees. Ollama is optional;
  its earlier direct CPU response took 32.26 seconds and exceeded the app budget.
  Provider privacy settings, ZDR and US-only residency remain unverified; see
  [privacy](https://github.com/jlhpacheco/solvia/blob/main/docs/PRIVACY.md). Use fictional adult details.
- **Hardening release check:** the final 390-test backend run passed in 12
  seconds on `92cba211`. It includes plate grounding/uncertainty, 1,000-character limits
  before inference, 50-character vehicle fields, named state/color and exact
  term/type vocabulary, referenced vehicle-state protection and provider
  selection without live provider calls. TypeScript checking passed. The
  initial Together run passed 366/367: delayed timer cancellation under load
  exposed a deadline failure. We added elapsed-time rejection and four
  deterministic regressions before passing 371/371. The historical 367-test
  checkpoint and earlier Meta 331/332 uncertainty failure are retained.
  Agent review also caught substring vocabulary acceptance. Test-host/browser
  harness failures remain retained, with successful reruns recorded separately.
- **Public AI admission:** the singleton guard shared by chat and the form
  permits 20 requests/client/minute and 60 globally, with two concurrent/client
  and four globally. It releases admissions on success, failure and cancellation.
  It is process-local and resets on restart; hosting still needs suitable
  provider spending controls and, for multiple instances, shared/gateway quotas.
- **Payment versus validity:** a stale cancel on a Paid record shows payment
  confirmation, not a new charge or an unconditional active permit. Receipt
  headings use `PermitValidity.Check` for dates and denial state. Unknown
  returns direct people to inspect records before trying another payment.
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
- [Opt-in live model tests](https://github.com/jlhpacheco/solvia/tree/main/tests/ai)
- [Opt-in Sandbox suites and boundaries](https://github.com/jlhpacheco/solvia/blob/main/docs/SANDBOX-MATRIX.md)
- [Dated payment results and references](https://github.com/jlhpacheco/solvia/blob/main/docs/SANDBOX-RESULTS-2026-10-19.md)
- [Reusable TypeScript Sandbox tests](https://github.com/jlhpacheco/solvia/tree/main/tests/sandbox)
- [Rules/readiness audit](https://github.com/jlhpacheco/solvia/blob/main/docs/HACKATHON-RULES-AUDIT.md)
- [Project license](https://github.com/jlhpacheco/solvia/blob/main/LICENSE) and [third-party notices](https://github.com/jlhpacheco/solvia/blob/main/NOTICES.md)

## Change and retest log

For each material change, append an entry with failures and corrections and
retain the tested source version with the evidence. A pass applies to that
version and scope. This working log is the starting record for that process.

| Record field | Historical October 19 hardening checkpoint |
|---|---|
| Date | October 19, 2026 |
| Tested source | `e3a49938bc08be6fc0d2fe11c695bf69adc8f9dc` (application code; documentation follows separately) |
| Change / risk | Together/Qwen reader, input/plate/enum/history guards, elapsed-time deadline rejection and credential replacement. |
| Observed checks | Backend 371/371 passed, zero skipped; TypeScript checking passed; 82 cumulative controlled browser cases, including fresh 30/30 smoke in 3.8 minutes. Eight distinct real Together cases passed twice, all `together`. |
| Evidence / remaining gate | `together-release-verified.trx`, retained failed `together-release.trx`, separate browser reports and both live-AI JSON reports. Wallet approval, judge access, account privacy settings and video remain open. |
| Review | Codex agent review; release-owner approval pending. |

Copy these fields for the next material change, including the exact tested SHA,
command, observed passes/failures, evidence link and remaining gate.

### October 20 follow-up release

Source `92cba211` passed 390/390 backend checks in 12 seconds. The desktop/phone suite passed 84/84 in 4.0 minutes, including 32 smokes. The tablet rerun on `eb488ab1` passed 16/16 in 2.3 minutes: 100 distinct controlled browser checks across two suites. The initial tablet 12/16 result exposed two navigation races and two real WebKit layout failures, corrected before the clean rerun. See TESTING for versioned evidence.

The finished 156.1-second narrated/captioned video shows three actual Together calls and a separate $30 Sandbox capture, independently checked for one capture and followed by PDF retrieval/privacy checks. It is excluded from the historical $945 matrix. Public YouTube publication, interactive wallet approval and clean full-path judge access remain separate gates.
