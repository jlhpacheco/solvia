# Relative parking dates — October 25, 2026

The reported sentence was: “I want a parking permit today and tomorrow as
well on wednesday. please. this is going to be a big week for me”. The old
reader extracted Wednesday but had no rule for today/tomorrow and therefore
left the duration/end date missing. A successful provider response did not
make that incomplete interpretation usable.

The reader now supports one-off lists containing **today**, **tomorrow**,
**day after tomorrow**, and singular weekday names. It captures one server
calendar date in **America/Los_Angeles**, the zone used by all current catalog
campuses. Tomorrow is calendar arithmetic, rather than a fixed UTC offset or
24-hour instant arithmetic across daylight-saving changes. The same captured
date is passed to the model as trusted developer context and to deterministic
grounding; a request crossing midnight cannot use two different anchors.

On Pacific October 25, 2026, the reported sentence resolves to exactly
**Sunday October 25, Monday October 26, and Wednesday October 28**. The form
fills October 25–28 and those three weekdays, displays the anchor and every
resolved ISO date, and retains the original sentence. “Big week” supplies no
duration. A bare weekday in this list means its first occurrence on or after
the captured date, within seven calendar dates. Duplicate dates collapse;
a list with one distinct date selects One visit.

The existing date/weekday controls must reproduce the exact resolved date
set. They cannot add an unstated date when the list becomes a plan. Stated
visit totals still have to agree. A rejected new reading clears the old
inferred end and weekdays. Recurring schedules, relative ranges such as
“today through Wednesday,” next/last/this-week qualifiers, conflicting weekday
annotations, mixed full numeric/named dates and unsupported offsets require
review instead of becoming a partial list. In particular, “two days after
tomorrow” cannot silently become “tomorrow.” This is bounded support for
explicit relative lists, not a claim to understand every natural-language
schedule or every possible spelling.

The planner's default date and past-date validation now use the same Pacific
calendar, independently of the server's local timezone. Prices, eligibility,
term bounds, protected prepaid choices and capture-confirmed issuance retain
their existing authorities. Reading and comparison create no purchase,
application or PayPal order/capture.

## Verification

The initial focused backend run passed **213/213**. The final full suite
passed **1,102/1,102**, adding 51 cases relative to the preceding checkpoint.
Fixtures use a UTC-host clock at October 26 06:59 UTC, while the California
date is still October 25. They verify the default form, parser, comparison
and prepaid choices all agree on the three dates, retained raw text, stale
draft replacement, single-visit tomorrow and unchanged database/payment state.
Calendar cases cover Pacific midnight, daylight-saving transitions, year
rollover, duplicate dates, uncertainty, conflicting annotations and offsets.

Ten distinct focused TypeScript browser cases are verified through initial
runs and a targeted rerun: four desktop Chromium, four phone Chromium, one
iPhone WebKit and one iPad WebKit emulation. Six smoke cases are included in
those ten. The new cases exercise the exact screenshot prose, replacing
unrelated previous dates/weekdays, no-JavaScript use, and every rendered visit
date/price. The prior numeric-range smoke is also retained. Seven of the
initial eight Chromium cases passed; the first desktop numeric-range journey
exceeded its overall 60-second budget. Its targeted rerun passed in 17.1
seconds with unchanged production code and deadlines. This checkpoint is
aggregate coverage, not one clean ten-case final run.

Both initial Apple emulations also exceeded the 60-second total journey
budget. Independent trace review found the correct anchor, bounds, selected
weekdays, three visits, $3/$30 totals and exact date list; no UI assertion
failed. The iPhone timeout occurred during the final details click and the
iPad timeout around the date-list assertion; both completed those assertions
after the deadline was dispatched. They still count as failed runs. The new
relative-date journeys now receive 90 seconds overall, including setup,
navigation and all field/comparison checks, while individual assertions keep
their shared 10-second deadlines. Both Apple cases were rerun without any
production change. The traces support a cumulative-runtime explanation;
they do not establish CPU contention as its cause.

The actual Together GPT-OSS 120B check passed with source `together`, the
captured Pacific anchor, every expected date and all corresponding weekday
controls. It retained the original sentence and created no comparison or
purchase. The first live attempt used the built-in fallback; that draft
already contained the correct dates, but the test intentionally required
actual model acceptance and therefore failed. The accepted retry used the
unchanged build. The exact cause of the first provider fallback was not
established; neither configuration nor a rules result is presented as proof
of model availability.

TypeScript and diff checks passed. The app runs on localhost:5156 with its
existing database. The [sanitized evidence record](Solvia-relative-dates-verification.json)
records source/report hashes and retains both the initial and accepted live
results. Browser device profiles are emulations, not physical-device
certification. The broader browser catalog and payment flows were not rerun;
no new actual PayPal order/capture was created.
