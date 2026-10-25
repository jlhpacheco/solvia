# Testing Solvia

## Selected-college policy notes — October 24, 2026

The [school-switch repair record](review/CAMPUS-POLICY-2026-10-24.md) and
[sanitized evidence](review/Solvia-campus-policy-verification.json) cover the
MJC note incorrectly shown under unrelated colleges. **920/920 backend
checks passed in the final full run**, including 15 new integration cases.
**30 distinct focused TypeScript browser cases are covered** through initial
runs and final targeted reruns: 10 desktop, 10 phone, five iPhone WebKit and
five iPad WebKit emulations. **Eight smoke cases are a subset** of those 30.
The final five affected reruns passed; this is an aggregate checkpoint, not
one clean 30-case final run. Initial fixture, scenario and cumulative-budget
failures remain documented. Production code did not change for the browser
harness corrections. TypeScript and diff checks passed. The broader browser
catalog was not rerun; no new actual AI request or PayPal capture occurred.

## Month-end schedule reading — October 24, 2026

The [month-end repair record](review/MONTH-END-2026-10-24.md) and
[sanitized evidence](review/Solvia-month-end-verification.json) cover the
reported “10/26/2026 until the end of November 2026” sentence. It fills
October 26–November 30, M/W/F, and 16 visits. **905/905 backend checks**,
**22/22 focused desktop/phone TypeScript Playwright checks**, and **4/4
iPhone/iPad WebKit emulations** passed in their final complete runs. The
**six smoke cases are a subset of those 26 controlled browser cases**.
One separate actual Together GPT-OSS 120B check passed with source
`together` and the correct dates/weekday controls. TypeScript and diff checks
passed. The first browser attempt timed out at server startup, with no
passing cases; final browser runs used the prebuilt final app. The broad
browser catalog was not rerun, and no new PayPal order/capture is claimed.

## Prepaid dated passes — October 24, 2026

The [prepaid implementation and verification record](review/PREPAID-DATES-2026-10-24.md) describes one shared PayPal payment, separate dated permits, exact selected-date review, safe unpaid revision, price/identity guards, future validity and individual/combined PDFs.

The final full backend run passed **854/854** checks. Two actual Sandbox purchases across student/public profiles and two colleges completed for **$12 in test funds**. Create/capture retries reused the same references; independent provider lookups found one capture per purchase. Four individual PDFs and two two-page packets passed content/reference validation. Anonymous public checks accepted both today's passes and rejected both tomorrow's passes until their dates. No wallet-popup approval, real-money payment, human-study result or physical-device certification is claimed.

The initial backend attempt aborted at test-host connection; it produced no test results. The first executed full run passed 838/848 and exposed five direct-controller null-helper errors plus five obsolete rendered-copy/HTML-encoding expectations. After corrections, 848/848 passed; two further revision/CSRF checks and stronger child-payment assertions were added, then a complete 850/850 run passed. A final batch review added protection against receipt numbers already staged on sibling permits and four deterministic collision regressions; the final complete 854/854 run passed. Initial provider and browser harness corrections are preserved in the feature record. The 210-case desktop/mobile catalog is covered by the initial 205-pass run plus the final six-case targeted rerun (all six passed, including all five original failures). The **64 main smoke cases are a subset**. The final complete iPhone/iPad WebKit run passed **12/12** cases. This is an aggregate browser checkpoint, not a claim of one clean 222-case final run; retained failures and timeout/selector corrections are detailed in the feature record. Earlier checkpoints below remain historical evidence.


## Responsive home screen — October 24, 2026

**30/30 TypeScript Playwright cases passed in one complete final run** using
`playwright.home.config.ts`. Five checks run in each of six projects: desktop
Chromium (1440×900), short-laptop Chromium (1280×720), iPhone SE WebKit,
iPhone 13 WebKit, landscape iPhone WebKit and iPad WebKit. The **six smoke
cases are a subset of 30**. Browser emulation is not physical-device certification.

Checks cover visual/DOM ordering, labeled roles and selected-person state,
44-pixel person/action targets, horizontal overflow, all three entry paths,
real resource destinations, AI-status disclosure bounds and native keyboard
operation without JavaScript. They reject uncaught browser errors and any
attempt to start a payment. **13/13 application integration checks passed**
against the final views. TypeScript and diff checks passed.

Raw final evidence is `.playwright-data/home-layout/results.json` and
`backend/home-layout-final.trx` within that directory. The first browser
attempt stopped at server build before executing cases; a later visual-review
run was intentionally stopped for layout corrections. Neither is counted in
the clean final 30-case result. The broader suite was not rerun. No new
real-model request or PayPal capture occurred.

See the [layout review, measurements and screenshots](review/HOME-LAYOUT-2026-10-24.md).

## Daily-permit clarification and demo cleanup — October 24, 2026

**64/64 focused backend checks passed** after the final page changes:
`AppIntegrationTests`, `RosterVerifyTests`, `PricingTests` and `FlowTests`.
The initial **30/30 desktop/phone TypeScript smoke cases passed**. After review
added MJC's exemption to more entry/payment pages, the expanded daily-permit
journey passed **2/2** again, covering selection, confirmation, the $2 total,
resumed checkout and fee-table labels. Those two identities overlap the 30;
they are not two extra distinct cases. **4/4 tablet navigation checks passed**
in Chromium and WebKit, portrait and landscape emulation. That makes 34 distinct
controlled browser identities covered through the initial and targeted runs,
not a single full final run. Main and tablet TypeScript typechecks passed.

The local SQLite cleanup removed 12 reviewed old unissued drafts after a
consistent backup, preserving all paid/free records, provider references,
people and vehicles. Integrity, foreign-key checks and surviving-row comparisons
passed. See the [policy sources and exact cleanup scope](review/DAILY-PERMITS-AND-DEMO-CLEANUP-2026-10-24.md).
Reports stay private under `.playwright-data/daily-permit/`: backend
`backend/daily-permit-final.trx`, `smoke-results.json`,
`selected-campus-final.json` and `tablet-final.json`. The broader suite
was not rerun; no new real-model call or PayPal capture is claimed.

For hands-on judging, see the [Fastlane Sandbox verification-code instructions](PAYPAL-TROUBLESHOOTING.md#judge-quick-start-fastlane-verification-code).
The payment page labels the test code **111111 (six ones)** above the payment options;
no SMS is sent. This is specific to Fastlane in Sandbox, not ordinary PayPal sign-in.

The October 24 payment-help update passed **14/14 focused backend tests** and
**6/6 TypeScript Playwright smoke checks** across desktop and phone. Checks cover
both Check your total and resumed Checkout, visible instructions above the official
buttons, readable mobile layout, and no order/capture from reading the help.
Backend cases hide the code for live, mismatched, lookalike-host and non-HTTPS
settings. These are page/configuration checks, not a new live OTP or payment test;
the code itself is documented by PayPal. Raw results are
`.playwright-data/fastlane-help/backend/fastlane-help-final.trx` and
`.playwright-data/fastlane-help/browser.json`. The broader suite was not rerun for
this text change.

<!-- DATE-ONLY-CHECKPOINT:BEGIN -->
## Historical date-only checkpoint - October 24, 2026

Repeating confirmation now uses start/end dates and weekdays, with no visible or hidden Weeks control. Exact date counts, one-visit mode, revision, protected drafts, browser history and no-JavaScript paths remain covered. The current [repair record and walkthrough](review/DATE-ONLY-2026-10-24.md) and [allowlisted proof](review/Solvia-date-only-verification.json) identify the source and report hashes.

**759/759 backend tests passed**. **104 distinct focused TypeScript Playwright cases are covered** through initial and targeted runs: 60 desktop/phone and 44 tablet emulations. **14 smoke cases are a subset**. The initial tablet run passed **41/44**; two assertions assumed Chromium-style invalid-date sanitization and one multi-edit journey exhausted its cumulative 60-second budget. Tests now require either native blocking or server date rejection, with no guessed date or comparison; only the multi-edit total budget becomes 90 seconds, leaving 10-second assertions unchanged. Final affected checks passed **2/2 main** and **8/8 tablet**. This is a distinct-case aggregate, not one clean 104-case final run; the broader browser catalog was not rerun.

Production application code was unchanged by those test corrections. Three synthetic history/pageshow compatibility cases are labeled separately from actual Back journeys. All 16 rendered walkthrough pages were reviewed; three manually entered planner screens were refreshed. No new real-model request, PayPal order/capture, human study or physical-device result is claimed. Earlier results remain dated evidence below.
<!-- DATE-ONLY-CHECKPOINT:END -->

## Historical exact-date-range checkpoint - earlier October 24, 2026

Source [`00da7d08857b04e8898dd85f84718774a4eeb099`](https://github.com/jlhpacheco/solvia/tree/00da7d08857b04e8898dd85f84718774a4eeb099) adds inclusive end dates, linked date/week controls and WebKit-safe comparison invalidation after Back. See the [repair record](review/DATE-RANGE-2026-10-24.md) and [allowlisted proof](review/Solvia-date-range-verification.json) for exact run counts, initial failures, reruns and hashes. Backend: **745/745**. Main browser: **180 distinct cases covered** via the full run plus final 54-case planner scope. Focused tablet range coverage: **24 distinct emulations** through initial and final targeted runs. These are **204 covered cases**, with smoke tests as a subset; they are not 204 executions in one clean run. The older 32-case general tablet suite was not rerun. One actual Together range read passed; no new PayPal capture occurred.

## Historical visit-type UX checkpoint — October 23, 2026

Source [`5edf8a11e9ab87219c5a4587dc9f0efa3be4f80f`](https://github.com/jlhpacheco/solvia/tree/5edf8a11e9ab87219c5a4587dc9f0efa3be4f80f) separates **One visit** (visit date only) from **Repeating visits** (start date, weeks, weekdays). See the [repair record](review/ONE-VISIT-MODE-2026-10-23.md), [actual screen](review/Solvia-one-visit-form.png) and [sanitized proof](review/Solvia-one-visit-mode-verification.json).

| Layer | Complete passing scope |
|---|---|
| Backend | **689/689 passed**, zero failed/skipped. `.playwright-data/one-visit-mode/backend/final.trx`. |
| Main TypeScript Playwright | **168/168 passed** in one run, desktop/phone; **50 smoke checks** are a subset. `.playwright-data/one-visit-mode/main-final.json`. |
| Tablet TypeScript Playwright | **32/32 passed** in one run, Chromium/WebKit portrait/landscape emulation. `.playwright-data/one-visit-mode/tablet-final.json`. |

The two browser runs cover **200 distinct controlled cases**. New checks exercise exact single-date calculation despite forged recurrence fields, owner/CSRF checks, invalid dates, mode freshness, legacy drafts, Back, handoff/revision, optional reading, no-JavaScript mode changes and hidden-field validation. Build (zero warnings/errors), TypeScript and JS syntax checks passed. Emulation is not physical-device certification; automated agent reviews are not human studies. No new PayPal capture occurred.

The real Together date-only check is **one distinct case, two attempts**: first a labeled fallback/failed strict source assertion, then a same-source accepted model result and passing application case in **4.506 seconds**. A separate bounded direct diagnostic returned HTTPError without a recorded status and did not identify the app fallback cause. Production/prompt were unchanged between the application attempts. This is not a clean two-pass sequence, uninterrupted reliability or a rerun of the full 13-case opt-in catalog. Raw reports: `.playwright-data/ai-evidence/one-visit-mode/results.json` and `one-visit-mode-recheck/results.json`. Older checkpoint results remain separately scoped below.

## Historical single-visit/status checkpoint — earlier October 23, 2026

Application and test source: [`83748c8dc305a5e1f9df98df3ea4faf2f846afa9`](https://github.com/jlhpacheco/solvia/tree/83748c8dc305a5e1f9df98df3ea4faf2f846afa9). See the [repair record](review/SINGLE-VISIT-AI-STATUS-2026-10-23.md) and [sanitized proof](review/Solvia-single-visit-AI-status-verification.json).

| Layer | Verified result and scope |
|---|---|
| Backend | **661/661 passed**, zero failed/skipped. Full C#/xUnit suite; `.playwright-data/single-visit-ai-status/backend/final.trx`. |
| Main TypeScript browser | **156 current distinct cases covered** across a full run and final targeted correction, desktop/phone. This is an aggregate, not a clean 156-case full run. |
| Smoke subset | **48/48 covered** within main; do not add these to its total. |
| Tablet TypeScript browser | **28/28 passed** in one clean run, Chromium/WebKit portrait/landscape emulation; not physical hardware certification. |
| Actual Together AI | **1 distinct exact-sentence case passed**, executed twice; final execution **1.929 seconds**. Validated `openai/gpt-oss-120b`, date 2026-10-26, Monday, one week and observed-status badge. This does not claim a rerun of the historical 12-case live suite. |

The current controlled browser catalog contains **184 distinct cases**: 156 main plus 28 tablet. The initial main run passed 152/154 and failed both pending-label checks. Browser-native form-action reflection exposed a real production bug; the held-navigation observation also needed a test correction. The final status suite passed 12/12, covering both vehicle/form action and planner/button override on desktop and phone. Its 8 overlapping passing identities plus 4 explicit pending-path identities cover the current 156-case catalog when combined with the initial 152 passes. Retired failed titles and rerun executions are not extra successful cases. Final JS/CSS corrections were checked by that 12-case status scope and the full 28-case tablet run; broad full-run C#/Razor source is unchanged. Raw initial failure reports remain retained.

The focused backend candidate first passed 137/139: two new tests stopped at an expected canonical Questions GET 302. Following that redirect corrected the test harness, then 142/142 focused checks and 661/661 full checks passed. A separate expanded-panel visual check found tablet left-edge clipping before the final left-anchor fix and responsive verification. `npm run typecheck`, JS syntax checking and the application/test build passed; the build had zero warnings/errors. Agent implementation/review is not a human study. These checks created **no new PayPal captures**.

Local raw reports: `.playwright-data/single-visit-ai-status/browser-final.json` (initial failed run), `status-final.json`, `main-catalog.json`, `tablet-final.json`; final real-provider report `.playwright-data/ai-evidence/single-visit-status-final/results.json`. Raw reports/traces stay private; the public proof contains counts, source, allowed status observations and evidence hashes only. Earlier checkpoints below retain their original versions and scopes.

<!-- SOLVIA-REPAIR-CHECKPOINT:BEGIN -->
## Historical guide-feedback repair checkpoint — 2026-10-21

Final application source: [`462d87dcb5de7fd24ab388ef826c7208db9e43d8`](https://github.com/jlhpacheco/solvia/tree/462d87dcb5de7fd24ab388ef826c7208db9e43d8). This checkpoint supersedes the earlier b8bf8d3 release while preserving the exact broad-baseline and final-delta verification scopes below.

**Source scope:** final application source is `462d87dcb5de7fd24ab388ef826c7208db9e43d8`. Its full backend suite and a focused browser check passed at that final source (24/24 focused checks in 313.108 seconds). The broad main/tablet browser coverage, actual hosted-AI cases and guide screenshots belong to baseline `9de5bd40f72a43565a56b5c6b6ffdceba08a41db`. They were not all rerun on the later shared-vehicle guard. The production delta is `src/Solvia/Controllers/PermitsController.cs`, `src/Solvia/Controllers/VehiclesController.cs`, `src/Solvia/Services/ApplicationService.cs`, `src/Solvia/Services/VehicleEditGuard.cs`, `src/Solvia/Views/Vehicles/Edit.cshtml`. Final focused checks are additional delta evidence, not extra distinct cases added to the baseline browser total.

| Final layer | Result and retained evidence |
|---|---|
| Backend | **603/603 passed**, zero failed/skipped, in **76.503 seconds**. `.playwright-data/navigation-fix/backend/repair-vehicle-lock-full.trx`. |
| Main controlled browser | **132 distinct cases covered** via a full run plus targeted rerun, in **1668.823 seconds** combined runner time. Final coverage has no unresolved failed/skipped/flaky/global-error cases. `.playwright-data/navigation-fix/browser-final.json`. |
| Tablet controlled browser | **24 distinct cases covered** via a full run plus targeted rerun, with reported durations retained but not treated as reliable elapsed measurements. `.playwright-data/navigation-fix/tablet-final.json`. Chromium/WebKit emulation is not a physical-device claim. |
| Smoke subset | **44/44 passed** within the main suite; not additional distinct executions. |
| Actual hosted AI | **12/12 passed**, zero failed/skipped, in **168.487 seconds**. `.playwright-data/ai-evidence/gpt-oss-repair/results.json`. Observed source `together`, pinned `openai/gpt-oss-120b`. No payment order/capture. |
| Refreshed guide | **21 pages**; full rendered visual review complete for its baseline source. PDF SHA-256 `e405a9faa5002f413cc1dc6bae2ebc989816410e99fd8a8e2eaf08a80ca39028`. |

That checkpoint's controlled browser total was **156**: main plus tablet. Live-model cases are separate and are not added to that total. Repeated runs do not create additional distinct cases. These are controlled checks and AI-assisted reviews, not a human usability study.

The main evidence is a distinct-case aggregate, not a clean 132/132 full run: the full run passed 131/132 with 1 overall test timeout(s), with runner duration 1570.596 seconds; the targeted rerun passed 1/1 with runner duration 98.226 seconds, resolving the same failed case identity/identities. Rerun executions are not added to the distinct total. The reviewed trace showed final UI assertions passed and HTTP 200/302 responses; the 30-second overall budget expired before cleanup/AfterHook completion. The trace does not establish a specific cleanup hang. Only that test's overall budget was increased to 60 seconds; assertion waits remain 10 seconds. Application code was unchanged between this layer's baseline full run and targeted rerun. Retained full-run evidence: `.playwright-data/navigation-fix/browser-final.json`; targeted evidence: `.playwright-data/navigation-fix/desktop-targeted.json`.

The tablet evidence is a distinct-case aggregate, not a clean 24/24 full run: the full run passed 22/24 with 2 overall test timeout(s), with reported, unreliable runner duration 17189.952 seconds; the targeted rerun passed 2/2 with runner duration 42.299 seconds, resolving the same failed case identity/identities. Rerun executions are not added to the distinct total. Two 60-second overall timeouts occurred while assertions were still active; those assertions are not counted as passed. Browser and test traces contain event gaps of 13,670,249 ms and 3,000,234 ms. The coincidence supports retaining a timing anomaly; a host interruption or other root cause is not proven. Reported runner durations are preserved but are not reliable elapsed/performance measurements. Application code was unchanged between this layer's baseline full run and targeted rerun. Retained full-run evidence: `.playwright-data/navigation-fix/tablet-final.json`; targeted evidence: `.playwright-data/navigation-fix/.playwright-data/navigation-fix/tablet-targeted.json`.

New regressions cover person-first Home roles, numeric plate/state extraction, retained original wording and manual corrections, repeated Back/read, explicit/no-JavaScript revise, changed wording, date/identity retention, owner/flow isolation, college changes, eligible SDK controls at the total, stale-tab purchase fingerprints, and owner-safe 429 recovery. `CheckoutReviewSnapshot` is a freshness check, not authentication; its transaction-time comparison rejects changed purchase facts before creating a new order. Payment status/order/capture values are excluded from the fingerprint to preserve unchanged retry recovery.

The historical real-provider matrix remains **14 completed captures totaling $945 gross Sandbox funds**, with its free permits and PDF checks. Separate recorded $30 transactions remain outside that matrix. These repairs, controlled callbacks, SDK-display checks and model reruns add no new capture to the matrix.

### Retained candidate failures

The first final-repair candidate executed 582 backend cases: 551 passed, 31 failed and zero skipped. Twenty-seven failures were direct EF Core `InvalidOperationException` results from resetting `IsLoaded` on a populated required navigation; four HTTP 500-versus-409 failures came from that same path. `repair-final.trx` and `first-final-candidate-source.json` retain the failing scope. The corrected implementation reloads the application and resolves/reloads Vehicle and PermitType by their authoritative foreign keys, assigning matching nonnull navigations; it does not reset `IsLoaded` or clear the tracker. A warm foreign-key-change regression was added.

The next backend run executed 583 cases: 582 passed, one failed and zero skipped. That remaining test expected a later `DbUpdateConcurrencyException`; the authoritative reload correctly rejected a changed $40 saved quote against the $30 catalog with `CheckoutPricingException` before any provider call. The test contract was corrected and strengthened to require zero Create/Get/Capture calls, an unchanged 4,000-cent database quote and no order ID. No production change was made for this expectation correction. The intermediate `repair-final-fixed.trx` remains a failed historical run. Only the final passing proof above counts as the current release result; these attempts are neither clean passes nor additional distinct cases. After that expectation correction, the broad verification baseline passed 583/583 backend cases at `9de5bd40f72a43565a56b5c6b6ffdceba08a41db`. A later shared-vehicle guard has its own final-source full backend and focused browser results; it does not relabel the baseline broad UI/model run.

The first 24-case guard browser run passed 22 and failed two garage-check assertions (desktop and phone). The test used page reload after an invalid Edit POST, which replayed the rejected red description; the application had correctly retained that invalid form. The test was corrected to request the garage index and reopen Edit with GET before asserting the saved blue description. Production did not change for this test correction. The complete same 24-case scope was rerun cleanly; its result is separate from the baseline browser aggregate. The original vehicle-guard-browser.json remains retained.

Sanitized checkpoint: [review/Solvia-repair-verification.json](review/Solvia-repair-verification.json). Repair mapping: [review/USER-REVIEW-2026-10-20.md](review/USER-REVIEW-2026-10-20.md). The dated video predates these UI changes; refreshed guide proof does not establish a public YouTube URL, clean hosted judge access, interactive wallet approval or final submission.
<!-- SOLVIA-REPAIR-CHECKPOINT:END -->

## Historical US-model release — October 20, 2026 (b8bf8d3 checkpoint)

At this historical checkpoint, the feature release used OpenAI's US-developed **`openai/gpt-oss-120b`**, served by Together AI. It includes visit planning and read-only Operations review. See [implementation boundaries](US-MODEL-AND-PLANNER.md) and the [quality-control record](QUALITY-CONTROL.md). Verified application/test source: [`b8bf8d3f480344e50c76e6f01f2336e96902f651`](https://github.com/jlhpacheco/solvia/tree/b8bf8d3f480344e50c76e6f01f2336e96902f651). Historical SHAs below identify what actually ran then.

| Historical layer | Result / evidence |
|---|---|
| Backend | **517/517 passed**, zero failed/skipped, **13 seconds**. `.playwright-data/backend-validation/gptoss-release-verified.trx`; separate `bin/UsModelTests` output. The first restricted-sandbox launch ran no tests because testhost IPC timed out; `gptoss-sandbox-ipc-aborted.trx` is retained. |
| Actual hosted AI | **12/12 distinct live cases** passed in **26.4 seconds**: eight vehicle + four schedule. All observed sources were `together`; both adapters require returned `response.model == openai/gpt-oss-120b` before accepting validated final content. No PayPal orders/payments. Sanitized local evidence: `outputs/Solvia-US-model-verification.json`. |
| Observed elapsed values | Eleven timed application/browser observations: median **1.352 seconds**, range **0.815–2.646 seconds**; the uncertainty case has no recorded elapsed value. Not provider-only latency, guaranteed performance or measured production cost. |
| Controlled browser | **132/132 passed**, zero failed/flaky/skipped/global errors: 108 main (54 desktop/54 phone) in **314.6 seconds**, plus 24 tablet in **121.3 seconds**. Main has 36 smoke executions (18 tagged titles × two projects); tablet is six cases × four portrait/landscape Chromium/WebKit projects, with no smoke tags. TypeScript checks, including tablet, passed. |
| Bounded UI rechecks | Student, public-person and staff checks were AI-assisted automated reviews, not human-study results. Staff's final read-only pass verified Not priced vs confirmed $0 and reconciled 15 rendered saved Paid rows/$975. No provider recheck or new payment occurred. |

The current 2,048-token completion cap covers reasoning and final content; reasoning is neither used as a draft nor logged. Both adapters enforce pinned identity, strict shape/grounding, refusal/tool/truncation rejection, measured elapsed deadlines and cancellation. Vehicle/schedule response envelopes are capped at 64/32 KiB. The normal 10-second response budget clamps to 1–90 when configured. The shared limiter covers vehicle/chat and schedule reads before inference. Planner comparison uses confirmed dates, category and integer catalog cents; continuation still purchases one permit. Operations is a demo-gated view of saved records, not settlement or production authentication.

The first expanded tablet run passed 23/24; a trace proved a deferred-checkout-script/mock double-load harness race. The harness now waits for the original adapter and checks one render/container; production checkout code did not change for that correction. A later attempt collected zero tests because a blank model configuration was rejected by strict startup validation; the controlled test configuration was corrected. Neither attempt is described as a clean pass.

Historical reports: `.playwright-data/main-final-us-report/index.html`, `.playwright-data/main-final-us-results.json`, and `.playwright-data/tablet-report/index.html`; parsed local summary `work/agent-usability/final-us-browser-summary.json`. The first tablet report/trace and startup issue remain separately retained under `work/agent-usability/tablet-first-failure-20261020`. Passing these controlled SDK cases does not add real payment or live-inference evidence.

## Historical browser and backend checkpoints — prior source/model

The backend suite uses C#/xUnit. Browser smoke and journey tests use
**TypeScript and Playwright Test** with desktop Chrome and Pixel 7-sized
Chromium contexts. Tests use fictional data only.
The historical complete desktop/phone TypeScript run passed **84/84 checks** in
**4.0 minutes**, including **32 smoke checks** and the existing SDK/error
contracts. Report: `.playwright-data/reports/final-browser`. This clean full
run supersedes the earlier 82-case aggregate checkpoint. It preceded the
last receipt-view validity correction; five rendered-receipt cases in the
final backend suite cover that final delta. A full browser rerun of that
view delta is not claimed. A separate tablet run passed 16/16 in 2.3 minutes
on `eb488ab136a6f0438ed31a8037c947981846dd84`, covering Chromium and WebKit
in portrait/landscape. Report: `.playwright-data/tablet-report`.
Together these suites cover 100 distinct controlled browser checks: 84
desktop/phone (including 32 smokes) plus 16 tablet cases, excluding live AI.

The initial tablet run passed 12/16. Two failures were harness navigation
races before person selection completed; two exposed real WebKit overflow
on My permits. A person-selection heading wait and the responsive table/card
breakpoint correction preceded the clean 16/16 rerun. This records the
failure and its correction rather than treating the first run as passed.

History remains separate: the earlier full run passed 71 checks, nine
targeted reruns corrected harness expectations, and two later loading-feedback
cases brought aggregate coverage to 82. Retained failures include stale copy
assertions, cold-start timing and loading-feedback harness expectations.
The 30/30 smoke run in 3.8 minutes on `e3a4993` and the earlier 2.6-minute
run on `aa6781a` are historical checkpoints; rerunning cases does not add
distinct tests.
At that historical checkpoint, TypeScript checking passed and the app build completed with zero warnings/errors.

## Backend and route regressions — command and historical checkpoints

```powershell
dotnet test tests/Solvia.Tests
```

The historical coordinated backend run passed **390/390 tests**, zero failed or
skipped, in **12 seconds**, against application source
`92cba2119af4c7d5be7b7b51e70c99f9f2aecb8e`. It covers domain pricing, ownership, checkout/capture
verification, webhooks, PDF contents, actual Program.cs dependency injection,
routing, Razor rendering, antiforgery, Together/Ollama validation and input
boundaries, shared AI admission quotas/concurrency and stale-payment/permit
validity messaging. Five real-rendered receipt cases distinguish active,
denied, future and expired paid permits, plus a future free permit. Payment
confirmation alone must not label an inactive permit live.

The initial Together run passed 366/367: under load, late timer
cancellation allowed an over-budget answer to reach validation. The fix
rejects answers after the measured deadline, and four deterministic deadline
guards were added. The failure and the final passing run are separate records.

This supersedes the historical 244-, 308-, 367-, 371- and 382-test checkpoints. The earlier
expanded Meta run passed 331/332: an ambiguous
`plate might be...` sentence exposed a false plate extraction. That failure
is retained. Its fix/regression and the vocabulary checks identified in a
separate agent review were covered by the historical 367-test passing run.
That earlier provider configuration is not the current hosted reader.

That historical TRX is retained locally under ignored
`.playwright-data/backend-validation/final-validity-release.trx`.
The historical 371-test result is `together-release-verified.trx`; the failed
Together run is `together-release.trx`. The historical pre-switch result is
`meta-release-verified.trx`. The fresh smoke report is under
`.playwright-data/reports/together-smoke`. These records remain separate.
The [quality-control record](QUALITY-CONTROL.md) describes
the review, correction, and retest process and its limits.

The shared AI limiter admits at most 20 requests per client and 60 globally
per minute, with at most two in-flight requests per client and four globally.
Vehicle/chat and schedule-reading routes share it; rejections return HTTP 429 with Retry-After
before model invocation. MVC rejection retains the answers. Offline checks
cover shared real-route quotas, window recovery, concurrency, exception and
cancellation release. These are per-process guards that reset on restart,
not authentication, distributed quotas or a provider billing cap.

## TypeScript browser tests

Install Node.js 20 or newer in addition to .NET 10, then run from the repo root:

```powershell
npm ci
npx playwright install chromium
npm run typecheck
npm run test:smoke
npm run test:e2e
```

For the separate emulated-tablet suite, install matching browsers and opt in
to both engines. These are touch/viewport checks, not physical iPad testing:

```powershell
npx playwright install chromium webkit
$env:SOLVIA_TABLET_WEBKIT = '1'
npx tsc --noEmit -p tsconfig.tablet.json
npx playwright test --config playwright.tablet.config.ts
```

The suite inherits the isolated base server, with hosted AI credentials
blanked. The current tablet suite contains 24 checks and desktop/phone contains 108; all 132 passed in the current US-model run above. Its earlier 16-case tablet checkpoint remains historical.

On Linux CI, use `npx playwright install --with-deps chromium` for browser
system dependencies. To inspect failures or run interactively:

```powershell
npm run test:e2e:report
npm run test:e2e:ui
```

Coverage includes all main navigation pages, actual CSS/JavaScript responses,
responsive page width, campus links, AG Grid quick search and numeric sorting,
CDN failure fallback, numeric/compound plate extraction, missing-plate
validation, student/public day and term choices, employee issuance and real
PDF download, and missing-antiforgery rejection.

The suite starts its own app on **127.0.0.1:5157** with a fresh SQLite file
under ignored `.playwright-data/`. It uses the `Testing` environment, no
user-secrets, no usable PayPal or hosted-model credentials, an unreachable Ollama
endpoint with `AI_PROVIDER=ollama`, and a
separate build output. It does not stop or edit the user's preview at 5156.
Testing loads the source build's static-asset manifest explicitly. Report
and failure traces are ignored by Git.
Controlled test hosts explicitly clear hosted-provider credentials, including
`TOGETHER_API_KEY` and `Together__ApiKey`; they must not inherit a hosted
model secret. Historical provider settings are also excluded from the host.

The real AG Grid 34.3.1 library is delivered from the pinned npm package to
the page's CDN request in tests. The main journey suite uses real application
routes and business outcomes. The separate fallback test deliberately blocks that request.
An actual CDN-loaded grid was also checked in the local preview browser.

`checkout-errors.spec.ts` separately simulates the PayPal SDK callbacks and
HTTP failure/pending responses, while using real permit and checkout pages.
It verifies that order IDs appear immediately and that failed, pending, or
unreadable capture responses stay on checkout with a visible message. These
contract tests are not evidence of a live PayPal capture.

Native PayPal/card buttons are the primary payment controls. The suite also
covers failed or stalled SDK loading/rendering, late completion after a
timeout, eligibility failure, and a protected redirect backup without a
duplicate payment action. These are controlled SDK tests. A separate pair
of checks against the actual remote SDK and JavaScript-disabled checkout
also passed; see the dated provider results below.

## Provider integration evidence and limits

The isolated Playwright suite stops before external buyer approval. Its SDK
contract tests simulate provider responses; they do not prove a provider
capture or real AI inference.

A separate check against the running app and the real PayPal **Sandbox**
completed on **2026-10-19 at 22:26:52 UTC**:

| Check | Verified result |
|---|---|
| Application | Maria, Fresno City College term permit, $30.00 USD |
| PayPal order | `1G1852006N6969449`, provider status `COMPLETED` |
| PayPal capture | `7WR24202DB5257942`, provider status `COMPLETED` |
| App capture endpoint | HTTP 200; `completed` true |
| App receipt | `SOL-2026-197973` |
| Paid permit PDF | One US Letter page; $30.00 USD and both provider IDs checked in extracted text and rendered output |

This check used PayPal's published Sandbox test card with the official
`confirm-payment-source` API, followed by the app's actual
`POST /api/paypal/capture` endpoint. An independent provider order query
matched the completed capture, exact amount, currency, and merchant. No raw
card-handling feature was added to Solvia. This was a test-money transaction.

Earlier in the same session, an unapproved order returned HTTP 409 with
`ORDER_NOT_APPROVED`, and redirect resume reused its existing approval URL.
The later completed capture supersedes that earlier pending status.

Wallet/Personal-buyer UI approval has **not** been verified. A Business
sandbox seller cannot buy from itself; use a separate **Personal** sandbox
buyer when testing that UI path. Never put credentials in test source,
reports, or commits. See [PayPal's sandbox account guide](https://developer.paypal.com/sandbox-testing/accounts).

Real application AI verification requires the selected provider's `together`
or `ollama` source label. The demo person switcher tests fictional ownership flows and is not
production authentication. CSV pricing is the accepted hackathon scope;
no real-time college fee feed is claimed.

## Multi-college Sandbox matrix

The subsequent opt-in real-provider suite passed **20 checks**: 16 permit
cases and four payment guards. The cases cover CSU Monterey Bay, CSU East
Bay, Fresno City College, and Modesto Junior College / Columbia College /
YCCD, each with Student and Public personas buying day and term permits.
Fourteen captures completed for **$945.00 USD in Sandbox test funds**; the two
YCCD term permits correctly issued free without PayPal transactions.

Independent read-only PDF validation checked all 16 documents for one-page
US Letter output, exact campus, plate, category, permit type, fee, receipt,
and payment references. Free documents correctly state `FREE - ISSUED` and
`Not required ($0 fee / exempt)` for provider references. Selected public day,
public term, and long-campus-name free documents were also rendered for
visual review.

See [matrix setup and scope](SANDBOX-MATRIX.md) and
[the dated provider results](SANDBOX-RESULTS-2026-10-19.md). These card/API
captures do not establish that interactive PayPal wallet checkout was tested.

## Opt-in live AI checks

`tests/ai/model-live.spec.ts` contains **eight vehicle live-provider cases**;
`tests/ai/schedule-live.spec.ts` now adds **five schedule cases** for Together.
The current opt-in catalog has **13 available cases**. The historical 12-case
US-model suite passed at its dated checkpoints; this repair ran only the new
exact-sentence case twice, not the complete 13-case catalog. Live cases remain
separate from the controlled browser counts above. The suite uses the already-running
loopback app (default port 5156), creates fictional vehicle/application drafts,
and stops before any PayPal order/payment. It does not start or reconfigure
the app. Positive extraction cases require the actual selected source;
negative/ambiguous inputs may safely produce a labeled fallback.

Configure the app first using [README provider setup](../README.md#step-4--select-an-optional-ai-provider).
Configure Together's server-only credential privately, then restart the app
with `AI_PROVIDER=together`. The browser runner never receives that key. Use
fictional adult details and review the [hosted data boundaries](PRIVACY.md).
`scripts/Set-TogetherCredentials.ps1` provides the hidden-prompt Development
setup; its user-secrets store is local configuration, not an encrypted vault.

```powershell
$env:SOLVIA_RUN_LIVE_AI = '1'
$env:SOLVIA_AI_PROVIDER = 'together'
$env:SOLVIA_AI_MODEL = 'openai/gpt-oss-120b'
$env:SOLVIA_AI_EVIDENCE_LABEL = 'gpt-oss-120b'
npx playwright test --config=playwright.ai.config.ts
Remove-Item Env:SOLVIA_RUN_LIVE_AI,Env:SOLVIA_AI_PROVIDER,Env:SOLVIA_AI_MODEL,Env:SOLVIA_AI_EVIDENCE_LABEL
```

For Ollama, set `SOLVIA_AI_PROVIDER=ollama` instead and ensure the app itself
uses `AI_PROVIDER=ollama` with Phi-4-mini available. The flags identify and
authorize the test; they do not switch the app's server provider. Optional
`SOLVIA_AI_BASE_URL` must remain loopback. Together calls are metered.
The four schedule cases are Together-only and are skipped for an Ollama run.

Evidence uses ignored `.playwright-data/ai-evidence/{evidence-label}/`;
the current command selects `gpt-oss-120b` so it does not overwrite the earlier provider-labelled Qwen checkpoint.
traces are off. Retain failures and source labels. Historically, the same eight Qwen/Together
application cases passed twice, including a final **8/8** run against
`e3a49938bc08be6fc0d2fe11c695bf69adc8f9dc` after deadline hardening and
credential replacement. Every case returned `source=together`; this is
**eight distinct live cases**, separate from the 84 controlled browser checks.
The full final eight-case run took 1.3 minutes. Its submit-to-review values
were 5,314, 3,898, 2,109, 2,538, 2,221, 1,741, 2,660 and 1,582 ms: median
2,379.5 ms, range 1,582–5,314 ms on this computer/run. This includes app and
review navigation time, not just model latency, and is not a service guarantee.
The fresh 30-check controlled smoke run also passed in 3.8 minutes.

For that historical Qwen run, the first report was `results-first-pass.json` and final report `results.json`,
both then under `.playwright-data/ai-evidence/together/`. Current live evidence is separately identified as GPT-OSS in `outputs/Solvia-US-model-verification.json`. Repeating cases does not
double the distinct-case count.

Historically, a warmed direct CPU Phi-4-mini response took 32.26 seconds
while the 10-second app budget fell back. That observation is separate from
the actual Together application proof. Together's key remains private in
Development user-secrets.
See [Together's structured-output reference](https://docs.together.ai/docs/inference/chat/structured-outputs)
for the hosted request contract.
