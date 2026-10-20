# Solvia prize strategy

Official pages reviewed October 19, 2026; implementation and saved-form evidence refreshed October 20. This is an evidence-based entry strategy, not an organizer eligibility decision or prediction of a win. Nothing in this review submits an entry or selects awards on Devpost.

## Target one overall award plus one sponsor award

The rules permit **one Grand Prize plus one Sponsor Prize**, or **one Honorable Mention plus one Sponsor Prize**. Do not plan to stack Grand and Honorable Mention awards, multiple overall awards, or multiple sponsor awards. Grand awards are $12,000/$8,000/$5,000; Honorable Mentions are $5,000 each. [Official rules, section 8](https://paypalaihackathon.devpost.com/rules).

| Sponsor award | Published award | Published tool-use condition |
|---|---|---|
| AG Grid, first | $5,000; one winner | Use an AG Grid tool |
| AG Grid, second | $2,000; one winner | Use an AG Grid tool |
| AG Grid, third | $1,000 each; three winners | Use an AG Grid tool |
| APIMatic | $1,000 plus six months Business; three winners | Use an APIMatic tool |

Source: [official prize table](https://paypalaihackathon.devpost.com/rules). These are available awards, not awards Solvia has earned.

**Recommendation:** aim for the Grand Prize evaluation while making **Best Use of AG Grid** the specific sponsor entry. **Best Use of PayPal + AI** is the most natural Honorable Mention fit for the demonstrated product. This is a choice of emphasis, not a separate project or a claim that judges must award either category. Agentic Commerce is a weaker story today: the model drafts vehicle details and has no autonomous payment tools.

## AG Grid: demonstrated use, but a competitive gap

The rules' broad tool-use condition supports entering the Community integration. However, the [official AG Grid award page](https://paypalaihackathon.devpost.com/details/aggrid) emphasizes polished dashboards, custom widgets, theming and layout tools, going beyond a standalone grid/chart, and experimenting with AG Studio's agent framework. This is materially more ambitious than our current fee table. Do not turn minimum tool-use evidence into a claim of strong sponsor fit or guaranteed acceptance.

What exists:

- `src/Solvia/Views/Shared/_FeeTable.cshtml` loads the pinned AG Grid Community 34.3.1 bundle and the actual catalog rows.
- `src/Solvia/wwwroot/js/feetable.aggrid.js` creates the grid with a Solvia-colored Quartz theme, quick search, column filtering/resizing, and numeric semester-price sorting. A genuine $0 remains distinct from an unavailable price.
- `/College/Fees` compares ten campuses. Its native table fallback preserves the data when the external bundle cannot load.
- `tests/e2e/solvia.spec.ts` exercises the real pinned library, search/count, numeric sort and blocked-CDN fallback. The separate 16-case tablet rerun passed in Chromium/WebKit portrait and landscape; this is device emulation, not a physical iPad check.

What does not exist: AG Studio, dashboard editing, custom Studio widgets, cross-filtered payment analytics, or a Studio agent. The AG Grid table must not be described as any of those.

The smallest useful next improvement would connect the comparison to a person's decision: select a campus/permit type directly from the table, show a source/price detail panel, and carry that selection into the application without retyping. Verify touch/keyboard use and preserve missing-price guards. This improves product value, but does **not** close the Studio-specific competitive gap by itself.

For a stronger sponsor attempt, a separate, compact AG Studio workspace could combine the public fee catalog with a custom permit-readiness widget and a bounded, read-only catalog assistant. Demonstrate an actual interaction rather than decorative charts or invented campus outcomes. This is a proposed feature, not shipped evidence. Keep payment and personal information out of hosted AI prompts. Studio adds commercial licensing and implementation scope: its [official pricing page](https://www.ag-grid.com/studio/license-pricing/) offers a 45-day trial. An October 19 trial would end around December 3, before judging finishes December 15. A documented extension or suitable license would be needed for uninterrupted judge access; do not silently replace the existing MIT Community dependency with an expiring trial.

## APIMatic: currently an artifact, not verified tool use

`docs/apimatic-context.md` is an authored description of Solvia's API. `docs/SPONSORS.md` explicitly records that the Context Plugin did not run. This alone is insufficient evidence that APIMatic helped build the PayPal integration.

The [official APIMatic page](https://paypalaihackathon.devpost.com/details/apimatic) promotes actual PayPal Context Plugin use. Its linked [subscription claim form](https://docs.google.com/forms/d/e/1FAIpQLScc2oCgAFACm7d6H3R4mgzv-O34DdyVnLIBNiwmUNggr_-hhA/viewform) asks for a project/repository showing plugin usage and a description; claims undergo manual verification. That form is a subscription claim, not a substitute for the hackathon submission. Its participant offer is one month Basic, separate from the winning-team offer.

Do not claim APIMatic used or enter on that basis today. If pursued later, use the official plugin for a concrete PayPal task, retain a sanitized usage record and resulting code/review evidence, and update the sponsor ledger accurately. Adding a logo, dependency name or hand-authored context file is not that evidence.

## Overall judging and submission evidence

The [event overview](https://paypalaihackathon.devpost.com/) evaluates technology, design, impact, innovation and presentation. Existing evidence maps as follows:

| Area | Demonstrated strength | Priority gap |
|---|---|---|
| Technology | Provider-confirmed Sandbox captures and durable references; actual Together vehicle extraction; server-controlled prices | Clean judge setup/access and interactive buyer-wallet verification |
| Design | Student/public day and term choices; editable vehicle draft; explicit totals; receipt/PDF; 100 controlled desktop/phone/tablet checks | First-time usability observation and physical-device verification |
| Impact | Specific campus parking journey and published price comparison | Actual user interviews or observed completion/error measurements; no invented savings |
| Innovation | Model drafts details; catalog sets price; person confirms; provider confirms money | Demonstrate why the sentence/review path helps compared with the conventional form |
| Presentation | Actual screens and verifiable transaction artifacts | Public YouTube demo under three minutes showing an intelligible end-to-end journey |

Keep the prize pitch anchored in the working payment journey. Sponsor screenshots should explain why the grid helps a person choose, rather than merely naming a library. A narrated screen recording is sufficient in principle; an on-camera presenter is not a published requirement.

Submission checks still to verify:

- The saved/read-back draft identifies **AG Grid Community**, PayPal and Together/Qwen3.5-9B, with optional Ollama/Phi-4-mini. AG Studio and APIMatic are not claimed as implemented.
- The Additional Info sponsor-tools answer selects AG Grid. No mandatory AG Grid/APIMatic hashtag was found on the reviewed public pages; final entrant attestations and submission remain open.
- Make the exact sponsor screen, source paths and reproduction steps easy for judges to find. Keep `SPONSORS.md` the implementation-status ledger.
- Finish the public short video and functional judge access. The [overview's submission requirements](https://paypalaihackathon.devpost.com/) allow complete repository run instructions or a hosted demo; hosting itself is not mandatory.
- Preserve dependency notices, participant eligibility/ownership confirmations and the final submission review. Those remain required regardless of prize strategy.

No new sponsor integration, deployment, code execution or submission is claimed by this document.
