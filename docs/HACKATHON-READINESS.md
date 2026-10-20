# PayPal AI Hackathon readiness

Reviewed October 19, 2026 against the [official rules](https://paypalaihackathon.devpost.com/rules)
and [event overview](https://paypalaihackathon.devpost.com/). This is a project
audit, not an organizer decision or confirmation of submission.

## Official requirements to finish

The submission deadline is **November 12, 2026 at noon Pacific**. Judging is
December 1-15; results are expected around December 21. Confirm participant
eligibility, team details, registration, and applicable geographic exclusions.

Submit a functional application meaningfully combining **PayPal Sandbox and
AI**, English instructions, a public source repository with an open-source
license, and a public YouTube demonstration shorter than three minutes.
Provide free judge access and identify the tools/models used. Existing work
is allowed when substantially updated during the event; disclose the starting
point and changes if that category applies. The participant confirmed Solvia
was first built during this hackathon and should be classified as New.

Sponsor prize entries require actual use of that sponsor's tool. Solvia's
working fee comparison uses AG Grid Community. Planned Elastic, Render,
Zapier, APIMatic, or KERNEL work is not evidence of completed integration.
The prize goal is the Grand Prize and an AG Grid sponsor prize where the
rules permit that combination; neither is earned automatically. The current
integration is Community, without an AG Charts/Studio or APIMatic execution
claim. Organizer assessment still determines eligibility and awards.

Check the linked rules before submitting, including IP permissions and
participant obligations. An existing Solvia draft is present on Devpost;
final submission and participant eligibility have not been verified.

## How the app will be judged

After the initial viability check, the five criteria have equal weight (20%
each). The table is our assessment, not an official numeric score.

| Criterion | Evidence in Solvia | What would improve the entry |
|---|---|---|
| Technological implementation | Shared application/pricing services; actual Sandbox capture, receipt and PDF verified; eight Together application cases plus final same-eight rerun passed; automated route/domain tests | Verify interactive buyer checkout and a clean judge-machine run |
| Design | Solvia navy/teal layout, clear total, person switcher, responsive workspace, fee search | Observe a first-time user completing a permit on a phone |
| Potential impact | Published fees, one application path, receipts and printable permits; sourced district parking-fund examples | Measure completion, corrections, staff minutes and total cost per permit in a consenting pilot; no achieved savings claim |
| Innovation/idea | Reviewable language drafting, campus comparison, deterministic pricing and verified capture in one journey | Demonstrate the combined workflow; do not claim online purchase or PDF permits are market firsts |
| Presentation | Finished 2:36 narrated/captioned actual-flow video, screenshot guide, documented setup and open-source notices; English Devpost draft saved/read back | Listen to the final narration, publish the required public YouTube video and verify judge access |

The actual Sandbox capture now provides provider payment evidence. The
Together application runs also provide real model evidence: eight distinct
cases passed twice with every source labeled `together`. Remaining gaps include
clean judge access, user validation and public video publication.
A fallback parser is useful resilience but is not counted as
model evidence. CSV fee data is the accepted hackathon
scope; live price synchronization is not claimed.

SJECCD's FY2025–26 adopted budget, printed p.47, shows a budgeted $1,995,985
Fund 10 transfer into Parking Fund 11 alongside $100,000 in parking fees.
That combined parking/transportation fund includes staffing and operations;
the transfer is not a software cost or Solvia savings. MPC and Kern CCD /
Bakersfield provide additional, differently scoped policy and financial
examples. See the [primary sources, fair competitor comparison and proposed
pilot](IMPACT-AND-DIFFERENTIATION.md). These institutions are research examples,
not Solvia partners, and CCC fee rules must not be applied to CSU campuses.

The multi-college Sandbox matrix subsequently passed all 20 checks: 16
student/public day/term permit cases across four campuses, plus four guards.
Fourteen actual provider captures totaled $945.00 USD in test funds and two
permits issued free. All 16 PDFs passed content validation. See the
[dated results](SANDBOX-RESULTS-2026-10-19.md) and
[repeatable matrix setup](SANDBOX-MATRIX.md).

Two additional real-provider negative checks confirmed that unapproved and
refused payments cannot issue a paid permit. The actual remote SDK displayed
PayPal and card options, and a no-JavaScript smoke check confirmed its
protected continuation. The complete desktop/phone TypeScript run passed
84/84 in 4.0 minutes, including 32 smokes. It preceded the final receipt-view
validity change; five rendered backend cases cover that delta. The final
backend suite passed 390/390 in 12 seconds, zero failed/skipped, on source
`92cba2119af4c7d5be7b7b51e70c99f9f2aecb8e`. Paid denied/future/expired records
retain payment facts without claiming a currently live permit.

Both public extraction routes share limits of 20 requests/client/minute and
60 globally, with two in flight per client and four globally. They return
429/Retry-After before model invocation and preserve MVC answers. These
process-local limits reset on restart and are not a provider billing cap.

Historical checkpoints remain: 371 backend passes and 82 cumulative browser
checks, including a fresh 30-check smoke run in 3.8 minutes on `e3a4993`.
The eight actual Together cases passed twice on that model-tested source
and remain separate from the controlled suite. No new eight-case live run
on `92cba211` is claimed. Interactive wallet approval remains unverified.
The tablet rerun passed 16/16 in 2.3 minutes on `eb488ab1`, covering Chromium
and WebKit in portrait/landscape. Together the two suites total 100 distinct
controlled browser checks, excluding live AI. The first 12/16 result exposed
two harness navigation races and two real WebKit My permits overflow
failures; the selection wait and table/card breakpoint were corrected before
the clean rerun. See [testing details](TESTING.md).

A separate 156.086-second recorded action sequence exercised three actual Together
calls and one additional $30 Sandbox capture, followed by paid PDF retrieval
and anonymous privacy checks. Order `5CV7753505063622B`, capture
`9AP215253S500404B`, was independently checked for a single completed capture.
This separate-database run is excluded from the historical matrix total.
The narrated/captioned MP4 is complete at 156.1 seconds. Final listening,
rights review and the public YouTube link remain open before submission.
The English story, technical/AI/testing fields and 8.26 MB review ZIP
were saved and read back from Devpost. The project remains Draft, 2 of 5;
the video URL is blank, and no eligibility or final-terms box was checked.

## Participant checklist

- [ ] Confirm eligibility, team membership, registration, and acceptance of the rules.
- [ ] Rotate the sandbox secret pasted in chat; set it privately with `scripts/Set-SandboxCredentials.ps1`.
- [x] Complete a real Sandbox capture and verify the paid receipt and PDF. The $30.00 USD order `1G1852006N6969449` completed with capture `7WR24202DB5257942` and receipt `SOL-2026-197973` using official test-card confirmation and the Solvia REST endpoint.
- [ ] Verify interactive wallet/Personal-buyer checkout separately; the merchant account cannot buy from itself.
- [x] Verify the default Together `Qwen/Qwen3.5-9B` reader through the actual app: eight distinct cases passed twice, every source `together`. The final run's submit-to-review median was 2.4 seconds, range 1.6–5.3 seconds on this computer, not a latency guarantee. Ollama remains optional.
- [x] Repeat the same eight live cases after credential replacement/deadline hardening and finish the fresh 30-check controlled smoke run: both passed on `e3a4993`; smoke took 3.8 minutes. These reruns do not add distinct cases.
- [x] Pass the complete 84-case desktop/phone run, including 32 smokes, and final 390-case backend release. The final receipt-view delta is covered by rendered backend cases rather than a claimed full browser rerun.
- [x] Complete the corrected tablet rerun: 16/16 passed in 2.3 minutes, giving 100 controlled browser checks across two suites.
- [x] Produce the 156.1-second English narrated/captioned demo, 21-page screenshot guide and five-page quality-control PDF; save/read back the current Devpost draft and review ZIP. Final listening and public video publication remain separate.
- [ ] If demonstrating webhook recovery, register an HTTPS sandbox listener, configure its webhook ID privately, and test a real verified event. No subscription was present when the dashboard was inspected.
- [x] Confirm the employee/free permit flow and generate a Letter-size PDF. A physical printer check is not claimed.
- [x] Exercise AG Grid sorting and campus search in the actual app.
- [x] Save Solvia's circular logo and public merchant name in the Sandbox business profile; keep the demo disclosure visible in the app.
- [ ] Prepare judge access for the full judging period; local instructions ship, hosting is not deployed.
- [x] Confirm project chronology: Solvia is New, first built during this hackathon, per the participant.
- [ ] Publish the finished 156.1-second narrated video publicly on YouTube and complete the English submission.
- [ ] Review and submit explicitly on Devpost. No submission has been made by this review.
