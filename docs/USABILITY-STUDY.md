# Usability review and measurement kit

Prepared October 20, 2026. **No human study has been conducted.** The current
plan is independent agent persona reviews after the new features are ready.
These are AI-assisted diagnostics, not recruited students, visitors, surveys
or measured campus impact. Human observation sheets remain blank for a
possible later study. The synthetic fixture validates reporting conventions
only and must never be presented as observations.

## Decision and evidence boundary

Decide whether Solvia's AI-assisted path makes the permit task easier without
introducing wrong vehicles, misunderstood prices or false payment confidence.
Compare it with Solvia's actual classic path, not an imagined competitor.
The prototype, published fee catalog and [impact hypotheses](IMPACT-AND-DIFFERENTIATION.md)
define the tasks; existing automated checks establish separate functional
evidence. Neither an agent's preference nor a fast model response establishes
real-user time savings, fewer support requests, ROI or adoption.

Kit files are in [`study/`](../study/README.md); the optional local reporter is
[`tools/usability-summary.py`](../tools/usability-summary.py). It accepts one
provenance at a time: `agent`, `human` or explicitly enabled `synthetic`.

## Run independent agent reviews now

Assign one reviewer each to Student, Public visitor and Returning permit
holder. Give the role task card, app URL and fictional scenario; ask the
reviewer to act through the actual available UI, recording what happened.
An agent must not invent a person's feelings, consent, age or institutional
affiliation. Report “agent acting through the Student demo,” not “student
participant.” Avoid telling reviewers the desired conclusion or mock scores.

1. Record the tested commit, browser/tool, viewport, fixture date, role,
   reviewer label and any prior project knowledge. A reviewer with the full
   development history is not a naive first-time user; say so.
2. Use unique fictional plates. Coordinate browser ownership and shared demo
   records; do not reset another reviewer’s work. Prefer separate browser
   contexts/fixtures if available, and document any shared-state constraint.
3. Complete the assigned UI task without reading production source first.
   Record URL, visible labels, actual outcome, help needed and concrete
   friction. Inspect source afterward only to diagnose a reproduced issue.
4. Stop before creating a PayPal order or buying anything. Use an existing
   verified paid fixture for the separate receipt/PDF task. A fresh unpaid
   draft followed by an older paid receipt must be described as two records.
5. Distinguish a product failure, tool failure, network failure and reviewer
   mistake. Save relevant screenshots only after checking for credentials or
   unrelated private data. Do not upload raw databases or API keys.
6. Record results with `provenance=agent` and `reviewer_id=AGENT-01` etc. Log
   observed duration only if actually timed; tool scheduling and prior
   knowledge make it unsuitable as a human usability benchmark. Describe
   findings and limitations even when every task succeeds.

The agent review schema is [`study/agent-review.template.csv`](../study/agent-review.template.csv).
Its grain is one reviewer × one task. Pass/fail is a diagnostic result, not a
population estimate. Preserve unresolved findings and failed attempts.

## Future human study: six to eight consenting adults

Recruiting human participants is **not part of the current agent review**.
If the project later runs a human pilot, aim for 6–8 adults: an equal number
of self-described students and public visitors where practical. Do not claim
the sample represents all campuses, disabled users or younger students. Use
participant codes only; keep contact/consent records separately from results.
Do not collect real plates, institutional IDs, emails or payment credentials.

Read this plain-language invitation before any observation:

> We are testing a parking-permit prototype, not testing you. Participation
> is optional. You may skip a question or stop at any time without a penalty.
> Use only the fictional vehicle and person provided. No real purchase or
> campus permit is involved. We will record task outcomes, elapsed time,
> corrections and comments under a code, without your name. Screens or audio
> will not be recorded unless you separately agree. Results may be summarized
> for this project; quotes will not be attributed without separate permission.
> Are you at least 18, do you understand this, and do you choose to take part?

This is proposed study wording, not evidence of consent or an institutional
research approval. Honor the person's answer; stop if consent is absent.
Set a disclosed retention period before recruitment and remove identifiers
from shareable notes. Do not infer consent from using the app.

### Counterbalanced paired tasks

The design compares two equivalent first-vehicle tasks per person. Each
participant uses the **same device, campus, role, permit type, dates and fee**
in both paths, with matched fictional vehicles differing only in plate suffix.
One task uses **Ask Solvia**; the other includes **Add a vehicle** and
**Use the classic form**. Include all those steps in the timer. Selecting an
already saved car in the classic path while timing new AI vehicle entry
would compare different jobs and is not allowed.

[`study/assignment-plan.csv`](../study/assignment-plan.csv) assigns eight
possible participants, role, AI-first/classic-first order and A/B vehicle
variant. It is a plan, not completed sessions. For six participants use rows
P01–P06, preserving three of each role and equal path order. Do not drop
unfavorable cases after observing results. Alternate matched vehicle variants
as well as path order to reduce carry-over effects.

- Start on Home with the assigned fictional person selected and no matching
  saved study plate. Both task cards provide the same facts. The classic task
  includes manual vehicle creation; the AI task includes model waiting and
  editable review. Do not give one path extra facts or prefilled data.
- End at the correct **Checkout** summary before any PayPal interaction.
  Include all navigation, validation, waiting and correction time. The classic
  and AI paths do not have identical intermediate pages; the shared endpoint
  is the reviewed, correct checkout summary, not the first submitted form.
- Use a five-minute task limit, disclosed beforehand. A task still unfinished
  at five minutes is a timeout; do not silently remove it from the denominator.
  A withdrawal is retained as abandoned without a guessed final duration.
- Ask the same neutral clarification questions after each task. Observe
  silently first; if the participant asks for help, give the smallest neutral
  prompt and record its count/time. Do not coach toward a faster AI result.
- Keep any fallback in the AI-path results with `reader_source=fallback`.
  A fallback attempt is not evidence of actual model use and is not removed
  to improve the AI-path completion rate.

### Shared PDF retrieval task

After both entry tasks, give each person a clearly identified **existing paid
fictional permit** under the assigned demo person. State that it is a separate
record from their unpaid task drafts. Ask them to find it in **My permits**,
download **Permit (PDF)**, identify its campus and validity, and explain how
they would print it. Time this once in
[`study/retrieval.template.csv`](../study/retrieval.template.csv), independently
of AI/classic comparison. Stop before invoking a physical printer; explain
that saving a PDF is not a physical-print validation.

## Measures and definitions

Select three outcomes and two guardrails rather than treating every click as
a KPI. All definitions and denominators must be fixed before observations.

| Measure | Definition / collection | Interpretation and limit |
|---|---|---|
| Correct checkout completion | Completed within the limit with all assigned vehicle/campus/category/type/date fields correct and the expected catalog total. Numerator correct completions; denominator all attempted tasks for that path, including timeouts and technical failures. | Main task outcome. Show numerator/denominator and failures, not only a percentage. |
| Task time | Seconds from the task-card start instruction on Home to the correct Checkout summary. Include model/network waiting, navigation, help and correction time. Report median/range for correct completions, alongside all failures and timeouts. | Compare paired times only when both tasks succeeded, explicitly naming the smaller paired denominator and its survivor bias. It is not total population time saved. |
| PDF retrieval | Success finding and downloading the assigned pre-existing paid permit and identifying its campus/validity. Record seconds and print-instruction understanding separately. | A common task after both entry paths; do not attribute its outcome to AI versus classic. |
| Correctness guardrail | Count wrong fields in the initial complete draft, corrective edits after that draft, and wrong fields still present at the endpoint. Normal first-entry typing is not a corrective edit; intentionally empty fields before drafting are not extraction errors. | A faster path with unresolved errors is not a win. Count each distinct wrong field once per checkpoint; log exact affected field names in notes. |
| Payment clarity guardrail | Without hints, ask the person to state the total, whether this draft is already paid, and whether the money/permit is real. Score against the actual screen: exact catalog total; unpaid; Sandbox/demo only. | Distinguishes a working button from understood payment truth. Do not imply that an order ID alone proves capture. |

Record help prompts and observed support seconds as diagnostics. Six to eight
sessions cannot establish campus-wide staffing savings. No savings target is
set before a baseline exists. If unresolved vehicle errors or false payment
confidence occur, investigate before making speed claims.

## Benchmarks and claims

**Controlled model benchmark:** run the opt-in actual model suite only with
appropriate credentials and authorization; record model/provider, version,
validated source, input scenario, correct fields, failures and submit-to-review
latency. Never count mocks or rules fallback as actual model inference.
The existing eight-case record is a small fictional scenario set, not a
population accuracy estimate. This kit runs no model calls.

**AI versus classic benchmark:** use the paired UI protocol above. It measures
the full paths, including the classic new-vehicle prerequisite. An automated
script that fills fields faster than a person is a functional check, not a
human completion-time study. Agent timing must be separately labeled and
cannot replace human timing.

Before reporting, check provenance, assignment, duplicate attempts, missing
values, comparable devices, app version, task-date validity, actual reader
source and every failed attempt. Publish the sample size and range; with
6–8 adults use descriptive results, not significance or broad causal claims.
Include mixed outcomes and explain any exclusions decided before analysis.

Current shareable claim: **“Independent agent task reviews are planned; a
human usability study and measured operational impact remain pending.”**
After agent reviews, replace “planned” only with their actual count and
observed findings, still explicitly identifying them as agent reviews.

The reporter refuses mixed provenance and requires an explicit flag for
synthetic data. It never turns synthetic or agent rows into human results.
Store actual observations privately unless their sanitized publication is
authorized; do not commit participant logs just because templates are public.
