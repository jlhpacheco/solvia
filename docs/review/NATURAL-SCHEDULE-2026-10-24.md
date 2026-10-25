# Natural schedule reading — October 24, 2026

The reported sentence was `10/26/2026 , monday to 10/30/2026,
Friday. for 3 visits, as well as Wednesday`. The old grounding rule
required `from` immediately before the first date and allowed only a bare
`to`/`through` between dates. It rejected the sentence, even when Together
returned a schema-valid answer. An empty accepted answer also displayed a
misleading “Read by Together AI” introduction.

The reader now accepts a range joined by `to`, `through`, or `until`, with
optional `from` and a weekday annotation beside a date. The reported range
fills October 26–30, 2026 and Monday/Wednesday/Friday: exactly three dates,
not five days or three weeks. A weekday annotation must match its date.
Unannotated range endpoints need not themselves be visit weekdays.

Full named-month dates are supported alongside full US and ISO dates:
`October 26, 2026`, `Oct 26th, 2026`, and `26 October 2026`. An explicit
year is required. The existing month-end handling still uses the actual
month length, including leap years, and does not infer a year rollover.
Plans remain limited to 182 inclusive days.

A private interpretation copy repairs clear weekday/month/week spelling
errors using unique one-edit matches, including adjacent transpositions,
plus a small explicit Wednesday alias list. Examples include `Monay`,
`Tuseday`, `Wendesday`, `Firday`, `Febuary`, `Novemeber`, and `weeeks`.
The original text remains visible and survives draft restoration. Canonical
spelling, ordinary-word collisions and numeric dates are preserved. The
same corrected copy is sent to the hosted model, while its answer must
still agree with the deterministic schedule facts. This is parsing and
prompting work, not fine-tuning or a model replacement.

Unknown calendar-shaped words and ambiguous corrections require review;
they cannot silently disappear from a mixed weekday list. Short ambiguous
initials such as `M/T` are not guessed. Numeric and written visit counts
through 182 are supported, including `thirty` and `one hundred and three`.
A stated total number of visits is
checked against the selected weekdays within the exact range. A stated
visits-per-week frequency is checked against the number of selected
weekdays instead. Conflicting totals, signed/decimal counts, malformed
dates and negation cannot become a successful schedule suggestion.

Incomplete model-shaped answers now say that Together responded **but
the schedule needs review**. Actual provider provenance is retained;
provider availability and successful interpretation are different claims.
The form clears the previous inferred end/weekdays after a rejected new
reading, preserves the entered words and an independently chosen start,
and requires correction before comparison.

No natural-language reader can guarantee understanding every possible
sentence or misspelling. Unclear cases remain editable, with an explicit
review message. The reader suggests dates and weekdays only. Catalog
rules calculate prices, and reading/comparing creates no application,
purchase, PayPal order or capture.

## Verification

The final full backend suite passed **1,051/1,051**, including 131 new cases
relative to the prior 920-test checkpoint. New coverage exercises spelling,
ordinary-word collisions, ambiguous/unknown labels, named dates, exact
boundaries, total versus weekly counts, negation, malformed input, incomplete
mocked provider answers and real offline MVC reading/comparison/continuation.
The integration fixture verifies exact offered prepaid dates, preserved raw
text, stale-draft replacement and unchanged applications/payment references.

Final controlled TypeScript Playwright runs passed **16/16**: six desktop
Chromium, six phone Chromium, two iPhone WebKit and two iPad WebKit emulations.
Six smoke cases are a subset. The Chromium journeys include no-JavaScript
reading, later retrieval, exact rendered visit dates, and removal of stale
suggestions after a contradictory count. The two Apple cases per profile
exercise the reported three-visit sentence and misspelled sixteen-visit
sentence. Device profiles are emulations, not physical-device certification.

Two separate actual Together GPT-OSS 120B checks passed with source `together`:
the reported three-visit sentence and the misspelled November sentence filled
the exact bounds and M/W/F controls, retained the raw text and created no
comparison or purchase. The host was rebuilt/restarted on port 5156 and its
planner returned HTTP 200. TypeScript and diff checks passed.

The first focused backend run passed 401/406: an overbroad unknown-word rule
treated ordinary `each`/`well` as calendar errors. Narrowing its proximity
check fixed all five failures. The first full run passed 1,043/1,044; a plural
`Mondays` prefix was mistaken for a contradictory start-date annotation.
That recurrence distinction was repaired, and the next full run passed
1,044/1,044. A final review found ignored written totals above 26; supporting
and validating bounded written totals added seven cases, followed by the
final 1,051/1,051 run. The initial twelve controlled browser cases also passed;
the final browser runs above use the final build. Earlier reports remain
private, and the [sanitized evidence record](Solvia-natural-schedule-verification.json)
stores final source/report hashes and the initial failure counters.

The broader browser catalog and PayPal payment flows were not rerun. This
change made no new actual PayPal order/capture and does not modify catalog
prices, payment rules, issued permits or the stored database schema.
