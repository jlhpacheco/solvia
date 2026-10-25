# Policy notes follow the selected college — October 24, 2026

## Report and change

The screenshot showed **Antelope Valley College** selected while the planner
still displayed MJC's student-parking exemption. The planner and classic
Apply form included that note unconditionally.

Both forms now render its initial visibility from the selected campus and
update it immediately when the college changes. Only the combined
MJC/Columbia/YCCD catalog choice displays the note; the text explicitly
identifies its source as MJC and directs Columbia users to check their own
rules. This repair does not establish a district-wide exemption or change
the catalog's prices.

Browser history restores the planner's selected college before synchronizing
the note. The same synchronization handles native selection changes and
form resets. Other campuses' users cannot see or focus the hidden MJC link;
browser checks also verify their college selector has no MJC exemption in
its accessible description. No page navigation, AI inference or application
submission occurs just because a selection changes.

Without JavaScript, **Update college information** refreshes the selected
school's note while preserving the posted answers. These dedicated actions
require antiforgery tokens; the planner also validates the current draft
owner. Neither action creates an application, purchase or PayPal order.
Invalid date attempts remain available for correction rather than becoming
a default date. When JavaScript is active, the fallback button is hidden
and becomes a non-submit button so it cannot intercept Enter on the form.

Questions, total/review and resumed checkout already scoped the MJC note
to their selected college. The all-campus fee table retains its explicitly
MJC-labeled note, and campus search/selection lists retain the note under
the relevant row. No new policy is inferred for the other nine colleges.

## Verification and review

The final full backend suite passed **920/920**, including 15 new MVC
integration cases. They exercise all catalog colleges through both update
actions, retained schedules and reader provenance, form picks, invalid
dates/colleges, server-resolved identity, stale planner ownership and CSRF.
Counting service replacements and database before/after snapshots confirm
that the refresh actions invoke no AI/PayPal service and preserve stored
applications, purchases, payment references, issuance references and vehicles.

The [sanitized evidence record](Solvia-campus-policy-verification.json)
identifies the source files and test reports. **30 distinct focused
TypeScript browser cases are covered** via initial runs and targeted reruns:
10 desktop Chromium, 10 phone Chromium, five iPhone WebKit and five iPad
WebKit emulations. Eight smoke cases are a subset. The final five affected
reruns passed, resolving all initial failures. This is an aggregate, not one
clean 30-case final run. Production code did not change for the browser
harness corrections. Coverage checks all ten college choices in each form, accessible
descriptions, fresh prices, actual Back navigation and no-JavaScript
preservation. The strengthened price journey first compares MJC's $0
term, then switches to Antelope without comparing: Back must keep that
stale free-school comparison hidden, and a fresh comparison must use
Antelope's catalog fee. Device profiles are emulations, not physical-device
certification; the broader browser catalog was not rerun.

Initial failures remain in the private reports. Seven of 74 initial backend
checks failed because the new fixture assumed a seeded Maria vehicle;
adding a fictional vehicle before the baseline snapshot corrected the
fixture, and the full 920-case run passed. The first browser version returned
to exactly the previously compared campus and wrongly demanded hiding its
still-matching prices. The revised case makes a real unsent campus change.
An iPhone all-ten-colleges case exceeded its overall 120-second budget;
the retained trace shows its policy/accessibility assertions completed,
with startup/navigation and sequential checks consuming the journey budget.
Only the two exhaustive ten-college journeys receive 180 seconds; individual
assertion limits remain ten seconds. These are test-harness corrections,
not claims that a failed original run passed.

Controlled browser servers use the tested prebuilt app, blank provider
credentials and isolated SQLite databases. Their temporary configurations
change only the launch command, report paths and device projects. The live
preview remains at `http://localhost:5156/Planner`, with its existing database
preserved. No new hosted-model call or actual PayPal capture is part of this
UI repair.

Repeat the new controlled checks with
`npx playwright test campus-policy.spec.ts`. Test evidence stays private
under `.playwright-data/campus-policy/`; source/report hashes and scoped
outcomes are published separately from those raw artifacts.
