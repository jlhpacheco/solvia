> Current prepaid checkout update: multiple visits can now be explicitly selected and paid in one order, with separate dated permits. See [prepaid checkout](review/PREPAID-DATES-2026-10-24.md). Earlier first-day-only observations below describe the previous flow.

# US-developed AI, permit planning and staff review

Updated October 24, 2026. This is the current implementation note; older
dated test records preserve the model and source that actually ran.

## Selected model and provider

Solvia selects **OpenAI's US-developed GPT-OSS 120B**, hosted through the
participant's existing **Together AI** account. The exact API model is
`openai/gpt-oss-120b`. OpenAI supplies the open-weight model; Together runs
the hosted inference service. No OpenAI API account or key is needed.

This replaces the previous Qwen reader at the participant's request.
The previous Qwen timings and tests are historical evidence, not tests of
GPT-OSS. No weights were fine-tuned or bundled with Solvia.

Primary sources checked October 20:

- [OpenAI model release and Apache-2.0 licensing](https://openai.com/index/introducing-gpt-oss/).
- [Together GPT-OSS contract](https://docs.together.ai/docs/gpt-oss): low,
  medium or high reasoning effort; reasoning cannot be disabled entirely.
- [Together current serverless catalog](https://docs.together.ai/docs/serverless/models):
  GPT-OSS 120B supports structured outputs; $0.15 per million input tokens
  and $0.60 per million output tokens.
- [Together deprecations](https://docs.together.ai/docs/deprecations):
  GPT-OSS 20B was removed from serverless inference September 15, 2026.
  A stale product/pricing page does not establish current API availability.

We use a private server key, fixed HTTPS endpoint, pinned model, low
reasoning effort and a bounded completion budget. Only final schema-shaped
content is read; reasoning is neither displayed nor logged. Model identity,
response shape, grounding, deadlines and cancellation are checked before a
result receives the Together source label. No model tools or payment access
are provided. A labeled rules fallback and editable fields remain available.

US model origin does **not** establish US-only processing, data residency,
zero retention, or a verified account privacy setting. Use fictional adult
personas and vehicles; see [privacy](PRIVACY.md).

For scale, 1,000 input tokens plus 200 total completion tokens would cost
about $0.00027 at those published rates. Actual completion usage includes
reasoning tokens, so that illustration is not a measured bill or a promised
cost per permit. A 2,048-token completion cap costs at most about $0.00123
in output tokens; input tokens and other service charges are additional.
Process-local request/concurrency limits are not provider billing limits.

## A schedule becomes an editable planning draft

The confirmation form separates **One visit** from **Repeating visits**.
One visit asks only for a visit date; its weekday comes from that date, and
no weeks or weekday selections are shown. A date-only sentence such as
“One visit on 10/27/2026” selects this mode. Repeating visits ask for
start/end dates and weekdays, with no visible or hidden Number of weeks form
control. An exact repeating range fills both date fields and selects its stated
weekdays. Calendar weeks, including a partial final week, remain an internal
compatibility value. Both modes survive revision and
browser Back; no-JavaScript users can apply a mode with an explicit protected
POST. See the [current date-only checkpoint](review/DATE-ONLY-2026-10-24.md).

The optional hosted reader's schema still contains only weekdays and a week
count; this is an internal contract, not another question for the person.
An independent strict parser also grounds one full US (`M/d/yyyy`) or ISO
(`yyyy-MM-dd`) start date or a strictly ordered `from ... to/through ...` range
in the person's sentence; the model cannot invent or extend those bounds.
“Tuesdays and Thursdays for three weeks” becomes editable dates and weekday
choices. The server converts that duration to an exact end date from the
selected start date. Existing weeks-only protected drafts and older callers
receive the same bounded conversion; a current date-only form with its end
cleared cannot recover an old duration from a hidden field.
“I am going 1 day 10/26/2026, Monday. All day. Only” supplies October 26
and its calendar weekday, resulting in one visit. A one-day-per-week schedule
for three weeks remains a repeating schedule. A recurring plan's start date is its planning
anchor and need not itself be a visiting weekday.
For example, Monday/Wednesday/Friday from November 1 to December 31, 2026
has an internal nine-week count while retaining December 31 as the exact end. A
separate term-window check still governs whether those visits are covered.
Malformed, partial, unrelated multiple dates or conflicting details require review;
missing duration is not silently filled with a default. A recognized date
replaces the previous form date and survives revision. Ambiguous or missing
facts remain open for confirmation; neither reader can set prices.
The deterministic planner counts confirmed visit dates and compares catalog
day-pass cost with a term permit whose demo dates cover the visits.

Fresno's researched fixture illustrates six visits at $1 each versus a $30
term permit. **Daily continuation starts one $1 pass for the first visit**;
it does not sell six prepaid visits. The chosen date survives vehicle review
and “Fix it.” Category is resolved on the server. Missing prices remain
unavailable, and comparison creates no application or PayPal order.

Term windows and employee exemptions are demonstration rules. The catalog is
a dated CSV snapshot, not a live fee feed, campus agreement or parking
reservation. Holidays, eligibility and availability need campus confirmation.

## Observed AI status

The header shows the selected provider/model, last validated response time,
and latest completed-read time. A configured key alone starts as **not checked**.
An adapter's accepted `together` response makes the indicator **response verified**
for ten minutes; an older response is labeled earlier, and the latest fallback
or interrupted read is identified separately. A current read is pending until
its actual result is known. Ollama and its rules-only schedule path are labeled
separately from hosted inference.

The read-only `/api/ai/status` endpoint uses two bounded process-local observation
lanes. It contains no sentences, vehicle fields, secrets or user identifiers,
does not call a model or database, and is not a provider health probe. Refreshing
status incurs no inference request. Restarting the app clears observed history;
past success does not guarantee the next request. Native disclosures retain
server-rendered information without JavaScript. See the
[dated repair record](review/SINGLE-VISIT-AI-STATUS-2026-10-23.md).

## A staff view of saved Sandbox records

`/Operations` uses AG Grid Community for filtering/sorting actual saved
application records, plus a native HTML table and GET filters when JavaScript
or the CDN is unavailable. Its counts and gross amount concern those saved
records, not real college revenue, PayPal settlement or a new provider lookup.
One application is one row. Unknown pending prices say **Not priced**;
confirmed free permits still show $0.00.

The feature requires Development or Testing **and** the explicit demo flag.
Production returns 404. It has no payment/model actions and does not select
names, emails, plates or verification tokens. This gate is appropriate for
this fictional demo; it is not production staff authentication.

## Agent reviews and fixes

Three agents reviewed Student, Public visitor and staff tasks through actual
rendered UI. These were **AI-assisted automated usability reviews**, not human
participants, interviews or measured adoption/ROI. They found:

- Historical paid day permits were called live after expiration. List and
  receipt now distinguish payment/issuance from current date/status validity;
  historical PDF retrieval remains available.
- Pending prices could appear as $0. The review now separates Not priced
  from an actual free/exempt permit.
- A successful planner could display an empty alert, and mobile results were
  below the long form. Valid comparisons show no empty error and move focus
  to the result heading. Keyboard and no-JavaScript paths remain usable.
- Public visitor wording now matches the seeded Public persona.

Final read-only agent rechecks passed these fixes. The staff recheck matched
15 saved paid rows to $975 gross in its isolated copied database. That includes
one previously recorded $30 case beyond the historical $945 matrix; it is
not a new capture or another independent payment-test total.

The [study kit](USABILITY-STUDY.md) contains blank human-observation templates
for future recruitment and separately labeled agent evidence. No real human
study result is claimed.

## Verification and presentation

Final model/release verification is recorded in [testing](TESTING.md) and
[quality control](QUALITY-CONTROL.md). The replacement video must show actual
GPT-OSS application inference and name the model/provider accurately. Reused
Sandbox payment footage is explicitly a separate recorded $30 term purchase;
it must not imply continuity with the new six-visit/$1-first-day example.
Historical Qwen AI footage is excluded from the replacement demonstration.
