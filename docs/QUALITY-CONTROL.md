# Solvia quality control process

**Working record — updated October 20, 2026.** We test a fictional campus-permit application and PayPal Sandbox integration. Our human project creator owns the release decision; agent assistance is not independent external certification or a real-user study.

## Current US-model feature release

The current implementation uses **OpenAI's US-developed `openai/gpt-oss-120b`, hosted by Together AI**. It includes editable visit planning, deterministic day/term comparisons and a read-only saved-payment Operations view. See the [current implementation note](US-MODEL-AND-PLANNER.md). The verified application/test source is [`b8bf8d3f480344e50c76e6f01f2336e96902f651`](https://github.com/jlhpacheco/solvia/tree/b8bf8d3f480344e50c76e6f01f2336e96902f651); older commits below remain historical.

| Layer | Latest verified evidence | Boundary |
|---|---|---|
| Backend | **517/517** C#/xUnit checks passed, zero failed/skipped, in **13 seconds** | Includes model identity, JSON/grounding, byte/deadline/cancellation limits, planner dates/prices, payment review, receipt validity and existing payment/ownership/PDF contracts. |
| Live AI | **12/12 distinct actual cases** passed in **26.4 seconds**: eight vehicle and four schedule cases, all observed sources `together` | Both adapters require returned `response.model == openai/gpt-oss-120b` before accepting final validated content. The suite creates no PayPal order or payment. |
| Controlled browser | **132/132 passed**: 108 desktop/phone in **314.6 seconds**, plus 24 tablet in **121.3 seconds**; zero failed/flaky/skipped/global errors | Main: 54 desktop + 54 phone, including 36 smoke executions (18 tagged titles × two). Tablet: six cases × four portrait/landscape Chromium/WebKit projects, no smoke tags. Separate from 12 live-model cases. |
| Agent UI review | Three AI-assisted role reviews and bounded final rechecks | Not human participants, recruitment, interviews, measured adoption or ROI. Staff recheck reconciled 15 rendered saved Paid rows to $975.00 gross; this is no new capture. |
| PayPal integration | Historical **20-check matrix**, plus **two separate real-provider negatives** and **two separate remote-SDK/no-JavaScript smokes** | Payment counts and actual references remain unchanged by the model switch. |

Eleven timed observations from the 12-case US-model run had a median of **1.352 seconds**, range **0.815–2.646 seconds**. One uncertainty case had no recorded elapsed value. These include browser/application flow time; they are not model-only latency or a guarantee. Sanitized local evidence is `outputs/Solvia-US-model-verification.json`; backend evidence is `.playwright-data/backend-validation/gptoss-release-verified.trx`. Controlled reports are `.playwright-data/main-final-us-report/index.html` and `.playwright-data/tablet-report/index.html`; the parsed local summary is `work/agent-usability/final-us-browser-summary.json`. TypeScript checks also passed, including the tablet configuration.

## Invariants drive our tests

| Risk | Acceptance rule |
|---|---|
| Wrong fee | Catalog and server-resolved category control integer-cent prices. AI and the browser cannot supply a fee. Missing published prices stay unavailable. |
| Planner overpromises | Confirmed weekdays/weeks produce actual dates within the demo term/permit window. The result is an estimate, not live campus eligibility, a reservation or a bulk purchase. Daily continuation starts only the first planned day. |
| False payment/issuance | An order is not payment. A paid permit requires a completed capture with the expected gross amount, USD currency and capture ID. Unapproved/declined outcomes cannot issue a paid permit. |
| Duplicate charge on retry | Repeated creation/capture reuses durable order, capture and receipt references; independent provider lookup shows one completed capture. |
| Corrupted identity | Editable review preserves the selected college/permit and validates plates, named state/color, 50-character vehicle fields and referenced vehicle-state history. |
| False AI success | Strict schemas and grounding precede the Together label. Unexpected returned model, tools, refusal, truncation, oversize response or exceeded deadline gives a labelled fallback; reasoning fields are not used or logged. |
| Public inference abuse | Vehicle/chat and schedule-reading routes share process-local admission limits before inference: 20/client/minute, 60 globally, two concurrent/client and four globally. 429/Retry-After preserves MVC answers. |
| Misleading saved records | Paid is separate from currently active. Unpriced pending drafts display Not priced; confirmed free/exempt records retain $0.00. Operations reports saved gross, not a new PayPal lookup, balance, fees or settlement. |
| Lost proof | Receipts and PDFs carry matching identifiers; public verification works without the buyer cookie and avoids exposing the full plate. |

Both model requests use a fixed HTTPS Together endpoint, pinned model, developer-role schema instructions, low reasoning effort, temperature 1 and a 2,048-token total completion cap. The default complete-response budget is 10 seconds, configurable from 1–90. Vehicle and schedule envelopes are bounded to 64/32 KiB respectively. Reasoning consumes completion tokens; no weights are trained or fine-tuned. Model origin does not establish US-only processing, ZDR or account privacy settings.

## Reproduce, fix, verify, review

1. We reproduce the failure with a URL, fictional input and expected/actual outcome, distinguishing app defects from provider, configuration and harness failures.
2. We fix the responsible boundary, then add a regression that would fail for the original defect and a useful valid boundary case.
3. We run focused checks and the relevant broader suite. Controlled provider callbacks test recovery; actual provider behavior needs separately authorized real Sandbox/live-model evidence.
4. We inspect rendered pages and selected PDFs, reconcile agent findings, review the diff and evidence, then commit/push authorized changes and verify the remote commit.
5. After an interrupted payment test, we query saved/provider status before another purchase. A failed assertion after payment must not encourage a blind second charge.

The first US-model backend launch aborted before running tests because the restricted test runner could not connect to testhost. We retained `gptoss-sandbox-ipc-aborted.trx`; the permitted local-IPC rerun passed 517/517. The first new tablet run passed 23/24: a deferred-script/mock double-load race was a harness defect. Its trace is retained; the harness now waits for the original load. A later attempt collected no tests because a blank model setting failed the new startup validation; the controlled configuration was corrected. Neither attempt is called a clean passing run.

## Payment evidence and versioned records

The separate real-provider matrix passed 20 checks across four campuses.
Fourteen completed captures totaled **$945.00 gross Sandbox test funds**;
two term permits were free. All 16 Student/Public PDFs passed one-page
Letter/content validation. Two further real-provider negatives stayed unpaid,
and two actual SDK/no-JavaScript smokes created no orders. The earlier
standalone $30 capture and the later recorded $30 term purchase are separate
from that matrix, not additional matrix captures.

The current **160-second** captioned demonstration uses freshly recorded
**OpenAI GPT-OSS 120B through Together AI** application inference. It shows
reviewable schedule/vehicle fields, the first-day $1 total, and a clearly
separate historical $30 term-payment/PDF example. Natural synthetic narration
retains its original tempo. Sampled frames and local transcription were
checked; full continuous listening/motion review remains separate.

Earlier implementation checkpoints are retained in the
[versioned testing history](TESTING.md). They are not counted as current
GPT-OSS evidence. This judge-facing report describes the current release;
changing the model does not retroactively change an older test result.

## Release gates and evidence index

Use fictional adult personas, fake plates and published Sandbox test cards. Credentials stay server-side; provider traces, raw databases and sensitive transport errors stay out of Git/public artifacts. A historical hosted-key inspection exposure was corrected by revocation/replacement and a separate same-case rerun. The demo person switcher and gated Operations page are not production identity or staff authorization.

Interactive Personal-buyer wallet approval, delivered verified webhooks, refunds, live-money processing, physical devices and actual Safari remain unverified. WebKit emulation does not establish physical iPad/Safari testing. CSV fees/demo dates are not a live feed or campus authorization. Clean full-path free judge access, real first-time-user feedback, final listening/rights review and public YouTube publication remain gates. The current Devpost draft remains 2 of 5, with personal/eligibility fields unfinished and final terms unchecked. The US-model story and judge fields have been saved; final attachment/read-back status is recorded separately.

Evidence: [testing/reproduction](TESTING.md), [current model/planner boundary](US-MODEL-AND-PLANNER.md), [Sandbox results](SANDBOX-RESULTS-2026-10-19.md), [matrix instructions](SANDBOX-MATRIX.md), [study kit](USABILITY-STUDY.md), [rules audit](HACKATHON-RULES-AUDIT.md), [privacy](PRIVACY.md), [license](../LICENSE) and [notices](../NOTICES.md).

For each later change, record: **date; exact source SHA; defect/risk; change; command and scope; initial failures; successful result; evidence path; reviewer; remaining gate**. A pass applies to its version and scope, not every later release.
