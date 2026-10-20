# Solvia — Use Case

**One student. One permit. One payment.**

Maria is a fictional adult student (18+). All people and vehicle details in
this demo are fictional, and all money is PayPal Sandbox test money. The
demo switcher is not production authentication or age verification.

## Meet the seeded cast

Solvia has no login stack — the demo people are seeded and fictional, each
on an `.example` address, so every pricing branch can be shown honestly:

| Person | Category | What they show |
|---|---|---|
| Maria | Student (`@student.example`) | The main story below |
| Diego | Student (`@student.example`) | Another student, another college, another price |
| Priya | Student (`@student.example`) | Another student |
| Sam | Student (`@student.example`) | Another student |
| Elena | Employee (`@college.example`) | Exempt — pays **$0** |
| Pat | Public (`@public.example`) | A member of the public — pays the full published fee |

A person's category is resolved once, by rule (a per-person override if one
is set, else the email domain), and **snapshotted onto each application at
creation** — pricing, the roster, and the permit PDF all read the snapshot.
Faculty and Visitor are real categories in the model; they price like
Public, and no seeded person resolves to them.

## The colleges — real fees, exactly 10

Solvia ships **exactly 10 California colleges, and every one of them has a
real published permit price.** The fee data is real research data: each
college's published parking-permit fee, read from its public page and kept
in `data/fees.csv`. Semester prices run from **FREE to $229.50**, with a
**median of $50**:

- CSU Monterey Bay — $229.50 · CSU East Bay — $195 · CSU Bakersfield — $93
- Contra Costa College — $50 · Diablo Valley College — $50 · Los Medanos College — $50
- State Center Community College District — $30 · **Fresno City College — $30**
- Antelope Valley College — $28 · Modesto Junior College / Columbia College / YCCD — **FREE**

Maria's college is **Fresno City College, $30** for a Fall/Spring term.
The demo does not claim Maria attends a real school or that a campus
endorses Solvia. The fee catalog is a dated research snapshot in CSV,
not a real-time campus feed. The total Maria approves comes from that
campus's researched published fee row. If a price is not published in the
catalog, Solvia says so and stops — it never invents a number.

## Two doors, one brain

There are **two doors into the same application brain** — both end at the
one creation path (`ApplicationService.CreateAsync`), the one price
function, and the one issuance path, so the two doors can never disagree
about a price or a permit:

1. **The form door — `/Permits/Apply`** (the reference-app shape): campus,
   vehicle, permit type, term, and start date on one plain form, one
   submit. No chat, no review step on this door.
2. **The AI door — `/Permits/Ask`**: pick a college, answer 3 questions
   only, read the AI's understanding back on an editable gate screen,
   review the exact total, then confirm. **This is Maria's door**, below.

Either way: a $0 application (a FREE campus, or Elena the Employee) is
issued on the spot with no payment due. A paid application goes to PayPal
checkout. Paid — and only Paid — makes the permit live.

## The flow, step by step (Maria, AI door)

```
MARIA — student, first day
        |
        |  "I need a parking permit."
        v
1. PICK A COLLEGE
   Maria picks her college first.
   (Find-your-college search over the 10 fee rows —
    the built-in match ships; an Elastic provider sits
    behind a config seam and the page names whichever
    provider actually answered. See docs/SPONSORS.md.)
        |
        v
2. THREE SHORT INPUTS
   College?  Car?  Permit choice?
   (College, permit type, and term are UI selections.
    The configured reader drafts vehicle fields only:
    Together/Qwen3.5-9B by default, or optional Ollama.
    An unavailable or unusable model
    answer uses the clearly labeled built-in reader.)
        |
        v
3. UNDERSTOOD — THE GATE
   The app shows back what it understood — college, car
   (plate, make, model, color, state), permit type, term —
   in editable fields. Nothing moves forward without a
   plate. Maria can fix any field here; submitting the
   gate saves her draft.
        |
        v
4. EXACT TOTAL — BEFORE ANY MONEY MOVES
   Review shows the exact total from her college's real
   published fee row: Fresno City College, Fall term —
   $30.00. Plus the permit dates. The extraction has
   NO price field — the AI can never set a price; the
   total always comes from the catalog.
        |
        v
5. MARIA SAYS YES
   She confirms. No approval, no payment.
        |
        v
6. PAYPAL PAYS — Sandbox
   PayPal Orders flow: create order -> Maria approves on
   the PayPal Sandbox page with a test buyer account ->
   capture. Result: COMPLETED, with a PayPal
   transaction (capture) ID.
        |
        v
7. DONE — PERMIT LIVE
   Only the COMPLETED capture (or a verified PayPal
   webhook saying the same thing) makes the permit live
   — never the "success" return page. The receipt shows
   the permit number and the PayPal transaction ID.
   Maria downloads a demo permit PDF. It grants no real
   campus parking authorization.
```

Step by step, in plain words: first Maria picks her college. Then she
describes her car and chooses her permit. The app shows her the drafted
vehicle details, and she checks them. Then she sees the exact total,
$30.00, from Fresno City College's real published fee. Then she says yes.
Then PayPal Sandbox takes the test payment and returns a transaction ID.
Then the permit goes live, the receipt carries that transaction ID, and
Maria downloads the permit PDF.

**Elena's shortcut:** Elena (`@college.example`, Employee) walks the same
doors and sees the same published fee resolved first — then her category
exempts her to **$0**, the application is issued at creation, and she
never sees a PayPal checkout. Pat (`@public.example`, Public) walks the
same doors and pays the college's full published fee, like any student.

## AI model choice — the honest framing

**Together AI with hosted `Qwen/Qwen3.5-9B` is the default; Ollama/Phi-4-mini
is optional.** `AI_PROVIDER` selects the adapter. The browser sends the
vehicle sentence to Solvia, which calls that provider on the server.
Together sends the sentence to its hosted API; Ollama's default endpoint is
local to the application host, which can be a server. Both adapters
use the same schema and plate safeguards. Use fictional adult details and
read [privacy](PRIVACY.md), including storage/training settings and residency
limits. [README setup](../README.md) documents the hidden-prompt Together
credential helper and the 10-second default timeout (clamped to 1–90).

Together's key is privately configured in Development user-secrets. The first
live application suite passed eight distinct cases with every source labeled
`together`, including the final release rerun after credential replacement and
deadline hardening. Historically,
a direct warm CPU Phi-4-mini response took 32.26 seconds; the application fell back
at its 10-second budget. The optional [live TypeScript checks](TESTING.md#opt-in-live-ai-checks)
require an actual selected-provider response; fallback is not presented as
a successful model demonstration.

What the AI does — and does not — do:

- The Questions flow uses the AI to draft vehicle details from the car
  sentence. College, permit type, and term are chosen in the UI. The extraction
  contract has no price field, and the person confirms the vehicle fields.
- Every price comes from the **catalog** (`data/fees.csv`), using the selected
  canonical campus and permit choice. The model cannot set the total.
- The honest source stamp travels with the application (`Source`):
  `"together"` or `"ollama"` when that configured model answered and passed
  validation, `"fallback"` when the deterministic reader answered instead
  (either provider unavailable, timed out, or unusable), and `"form"` for
  the form door. The review screen lets the person correct missing or
  uncertain details before creating the application.

## The college side — the roster

The college gets its own view of the same truth: **`/College/Roster`** —
who paid, who didn't, for one campus at a time (it opens on Fresno City
College). Every seeded student gets one row in big plain words:

- **PAID** — paid through PayPal; the row shows the permit number and dates.
- **FREE — ISSUED** — a $0 campus; the permit was issued with no payment due.
- **PENDING** — an application exists at this campus, but the money never landed.
- **NO PERMIT** — no application at this campus at all. Never invented data.

Over the rows sit three tiles, computed for that campus only:

- **Issued** — total permits issued (Paid + free-issued).
- **Revenue** — the sum of the Paid price snapshots.
- **Expired** — issued permits whose end date is already past.

Below the rows, a **By student** section lists each student with how many
issued permits they hold at that campus and the receipt (permit) numbers of
those permits, most permits first. An application that never issued is not
a permit and does not count. Roster truth comes from the same validity
logic as verification — payment state at a campus — never re-derived on
the page.

## Anyone can check a permit — `/verify` + `/api/verify`

A campus officer holding Maria's permit does not need a Solvia login. At
issuance, every permit gets an unguessable verify token, and that token is
the credential:

- **`/verify?t={token}`** — the public page. It describes only the one
  permit the token names: permit type, plate **state** (never the plate
  itself), the buyer's category, the payment status word, the last-updated
  stamp, and a plain-words verdict — **VALID**, or **NOT VALID** with the
  reason ("This permit has not been paid for…", "This permit ended on…").
- **`/api/verify?t={token}`** — the JSON twin: same token, same verdict,
  same field set, built from the same view model — not a parallel
  implementation.

Both doors are anonymous on purpose, share a per-IP rate limit
(60/minute; a tripped limit answers 429), and answer an unknown or missing
token with the same calm not-found result — no student name, no email, and
never the plate rides along.

A permit counts as valid when today falls inside its dates, it was not
denied, and its payment is Paid (or NotRequired for a free permit). A
refunded permit is never valid.

## Why this matters

- Today, at the incumbent store (mycampuspermit.com), many campuses do not publish their semester price on the public page at all — in the 2026-10-18 research scan of 25 campus pages, only 10 listed a student semester price publicly; the rest hide it behind a login or do not list it. (That scan describes the incumbent's pages, not Solvia's campus count.) Solvia ships exactly those 10 campuses — every one with a published semester price. A student can finish a long form without knowing the total.
- Prices also differ a lot by college (the 10 shipped semester prices run from FREE to $229.50, median $50), so one generic price would be wrong. The total must come from her college's real row.
- Maria's case fixes both: the price comes first, from her college, and she only approves — the AI and PayPal do the steps.

## What Maria never does

- Fill a long form.
- Guess a price.
- Pay real money in this demo (Sandbox only).
- Share real personal data (the demo student is fictional).
