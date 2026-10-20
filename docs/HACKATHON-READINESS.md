# PayPal AI Hackathon readiness

Technical evidence updated October 20, 2026. Rules were reviewed October 19 against the [official rules](https://paypalaihackathon.devpost.com/rules) and [event overview](https://paypalaihackathon.devpost.com/). This is our project audit, not an organizer decision, legal signoff, official score or confirmation of submission.

## Current US-model feature release

The default reader is OpenAI's US-developed **`openai/gpt-oss-120b`**, hosted by Together AI. It supports editable vehicle and schedule drafting. The deterministic planner compares confirmed visits against published catalog rates/demo date windows; daily continuation starts one first-day pass, not a bulk purchase. The gated Operations page reviews saved records and does not contact PayPal. See [current model/planner details](US-MODEL-AND-PLANNER.md).

- **Backend:** 517/517 passed, zero failed/skipped, in 13 seconds; `gptoss-release-verified.trx`.
- **Actual model:** 12/12 distinct live cases passed in 26.4 seconds (eight vehicle, four schedule), all observed sources Together; both adapters verify the exact returned model ID. Eleven timed application/browser observations had median 1.352 seconds, range 0.815–2.646; not provider-only latency or a guarantee. No payment was created by this suite.
- **Controlled browser:** 132/132 passed, zero failed/flaky/skipped/global errors: 108 main (54 desktop/54 phone, including 36 smoke executions) in 314.6 seconds, plus 24 tablet across portrait/landscape Chromium/WebKit in 121.3 seconds. TypeScript checks passed. These are separate from 12 actual live-model cases.
- **Payment:** historical 20-case matrix remains 14 completed captures/$945 gross Sandbox funds, two free terms and 16 validated PDFs. Two separate provider negatives stayed unpaid; two separate actual SDK/no-JavaScript smokes created no orders. The original $30 and later recorded $30 purchases remain separate.
- **User evidence:** three role-based agent reviews and final rechecks are AI-assisted automation, not human studies or measured savings. The staff UI recheck matched 15 saved paid rows to $975 in a copied demo database; this is no new capture or settlement proof.

Verified application/test source: [`b8bf8d3f480344e50c76e6f01f2336e96902f651`](https://github.com/jlhpacheco/solvia/tree/b8bf8d3f480344e50c76e6f01f2336e96902f651); earlier SHAs below remain historical. Model origin does not establish US-only processing, residency, ZDR or account privacy settings.

## Official requirements and submission gates

Deadline: **November 12, 2026, noon Pacific**. Judging runs **December 1, 2 p.m.–December 15, 8 a.m. Pacific**; the expected announcement is around December 21, 2 p.m. Pacific. Keep free functional judge access through the judging end.

Required artifacts include a meaningful PayPal Sandbox + AI application, English materials, public source with an open-source license and complete setup/run instructions or a working hosted build, and a **public YouTube video shorter than three minutes**. Hosting is an option, not a mandatory rule. A localhost address is not remotely accessible judge access. The participant confirmed Solvia is independent and first built during this hackathon: **New**; this does not establish every IP or eligibility fact.

Solvia's demonstrated sponsor is **AG Grid Community**, in fees and saved-payment review, with native fallbacks. Planned Elastic/Render/Zapier/APIMatic/KERNEL work does not establish execution or a sponsor prize. The goal is Grand Prize plus an AG Grid sponsor prize only as permitted by the rules; no award or eligibility result is predicted.

The current Devpost snapshot is **Draft, 2 of 5**, with a blank public-video URL and unchecked eligibility 16–18/final terms. The US-model story, tags, four judge fields and two safe screenshots were saved and read back. The prior attachment was replaced with **Solvia-US-model-QA.zip**. Personal answers, rights and eligibility remain participant work. See the [dated release record](RELEASE-2026-10-20.md).

## Five equally weighted judging criteria

Each criterion carries 20% after Stage One's pass/fail viability review. These are evidence/gap notes, not official numeric scores.

| Criterion | Current substance | Remaining evidence |
|---|---|---|
| Implementation | Actual US-model inference; deterministic planner; exact-money Sandbox capture/retries/PDF; read-only saved records; backend/browser regressions | Interactive wallet approval and clean full-path judge setup |
| Design | Editable human review, clear totals, day/term choice, responsive workflows and source/validity labels | Consenting first-time users and physical-device testing |
| Potential impact | Student/public paid permits and employee demo exemption; sourced district budgets/policies; planning estimate | Measure completion, corrections, staff minutes and cost in a pilot; no achieved ROI claim |
| Innovation / idea | Reviewable schedule-to-permit comparison, server pricing and verified payment/proof together | Demonstrate the combined difference; no market-first or proven autonomous-commerce claim |
| Presentation | Existing English narration/captions, guide and QA record; new US-model presentation capture is separate | Final listening/rights review, public YouTube and clean free full-path judge access |

SJECCD's FY2025–26 adopted budget, printed p.47, shows a budgeted $1,995,985 Fund 10 transfer into Parking Fund 11 alongside $100,000 parking fees. The combined fund includes staffing/operations; the transfer is not software cost or Solvia savings. MPC and Kern CCD/Bakersfield are differently scoped examples, not partners. See [primary sources, fair comparison and proposed pilot](IMPACT-AND-DIFFERENTIATION.md); CCC rules must not be applied to CSU campuses.

## Historical evidence — preserve source and model boundaries

The previous Qwen reader's eight distinct live cases passed twice on `e3a4993`, with final median 2.4 seconds/range 1.6–5.3. That checkpoint passed 371 backend and 82 aggregate controlled-browser checks, including fresh 30-smoke evidence. Source `92cba211` later passed 390/390 in 12 seconds; desktop/phone 84/84 plus tablet 16/16 on `eb488ab1` gave 100 controlled cases. The initial tablet 12/16 included harness races and actual WebKit layout defects, fixed before clean rerun. None is retroactively relabelled GPT-OSS.

The historical 156.1-second narrated/captioned video used Qwen-era AI and a separate recorded $30 capture, order `5CV7753505063622B` / capture `9AP215253S500404B`, independently checked for one capture and followed by PDF/privacy checks. It is excluded from $945. Any replacement US-model video must name its actual model/provider and distinguish reused Sandbox footage. Current artifacts are a refreshed/rendered **21-page guide**, **three-page QA report** and **160-second US-model video**; public YouTube publication remains open.

The first expanded tablet run passed 23/24 because of a deferred-script/mock double-load harness race; its trace was retained. A later startup attempt collected no tests due to a blank model setting rejected by strict US-model validation; the controlled configuration was corrected. These are retained failed attempts, not clean passes. See [testing](TESTING.md) for the final current runs when complete.

## Participant checklist

- [ ] Confirm eligibility, actual team/representation/jurisdiction, registration, conflicts, rights and acceptance of rules. The assistant does not check these attestations.
- [ ] Rotate the Sandbox secret previously pasted in chat and provision it privately; no completion is inferred.
- [x] Verify actual Sandbox capture/receipt/PDF and the historical multi-campus matrix; no real money is involved.
- [ ] Verify a separate Personal Sandbox buyer's interactive wallet approval; the merchant cannot buy from itself.
- [x] Verify current US-developed GPT-OSS through 12 actual app cases; source/model identity is checked, not inferred from fallback.
- [x] Pass current 517-case backend verification, zero failed/skipped, in 13 seconds.
- [x] Complete current tablet 24/24 in 121.3 seconds, zero failed/flaky/skipped. This is browser-engine emulation, not physical iPad/Safari testing.
- [x] Complete current 108-case desktop/phone rerun in 314.6 seconds, zero failed/flaky/skipped/global errors. Together with tablet, this is 132 controlled checks; earlier Qwen passes stay historical.
- [ ] Refresh presentation/QC/guide/review-pack artifacts to match the selected US-model release, inspect renders and listen to narration.
- [ ] Publish the finished English demonstration **publicly on YouTube**, under three minutes, and save its actual URL.
- [ ] Validate clean full-path free judge access through December 15, 8 a.m. Pacific. Local setup ships; hosting is not deployed.
- [ ] If claiming webhook recovery, register/configure an HTTPS Sandbox listener and test a real verified delivered event. Refunds/live money remain unverified.
- [x] Verify employee/free permit PDF output and actual AG Grid use; a physical printer is not tested.
- [x] Confirm participant-stated independent New project chronology.
- [x] Save/read back the US-model story, fields and current QA attachment without filling personal facts or accepting terms.
- [ ] Explicitly submit only when the participant is ready, then verify the actual confirmation/receipt. No final submission is recorded.
