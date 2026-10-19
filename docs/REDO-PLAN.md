# REDO PLAN — Solvia, rebuilt on Duvara's permit-application blueprint

**Date:** Oct 18, 2026 (evening) · **Architect output** — Jose Luis's order:
redo the code all over again on Duvara's permit-application blueprint,
every piece, nothing missed. **Fresh code written to Duvara's structure —
no Duvara code, config values, or secrets are copied.** Duvara /
CarPermitApp is read-only reference only and is never edited.

**Inputs (the law, in order):**
`DUVARA-PERMIT-CHECKLIST.md` (60 items) · the frozen current tree at
`solvia-merge/` (repo HEAD `595ea23e` — every HAVE verdict below was
checked against that code, not memory) · `solvia-prep/BUILD-PLAN.md`
(Solvia's existing shape and fixed decisions, which stand unless this
plan overrides them by name) · `lyceum/LESSONS.md`.

**Where the redo lives:** a brand-new solution in
`~/workspace/paypal-hackathon/solvia-redo/`, shaped like a repo root
(`src/`, `tests/`, `data/`, `docs/`, …). The current tree
(`solvia-merge/`, and the live repo at `595ea23e`) is the **frozen
fallback** — nobody edits it during the redo. The swap (§4) is one push.

**What "redo" means and does not mean.** The permit core — Apply form,
PermitType catalog, application row, checkout guards, issuance, My
Permits, receipt PDF, verify — is rebuilt to Duvara's structure, item by
item (§1). Solvia's hackathon layer (§2e) is rebuilt around that core
with its behavior pinned by the frozen tree's tests used as *spec*.
Nothing is copied file-for-file from either Duvara or the frozen tree;
builders write fresh code against this plan's contracts.

---

## 1. Disposition table — all 60 checklist items

Legend: **PORT** build it (it is absent or wrong-shaped today) ·
**ADAPT** Solvia has the substance; rebuild in Duvara's shape with the
named delta · **HAVE** already true in the frozen tree — the redo
rebuilds it identically and the checker verifies · **OUT** excluded,
reason stated.

### A. Apply form
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 1 | PORT | New Duvara-shaped form door: `GET /Permits/Apply` loads the current demo person's own vehicles + Active permit types only, types ordered by Name. Solvia addition (multi-college product): a Campus dropdown sits first, fed by `IFeeCatalog`. (Tonight's college-pick page moves to the AI door, `/Permits/Ask`.) |
| 2 | PORT | Vehicle dropdown, required; option label = `Plate · State · Make Model`; the POST re-loads the vehicle and rejects any id not owned by the current demo person with the general invalid-selection error. |
| 3 | PORT | Permit Type dropdown, required; options = Active types only, label = Name only. |
| 4 | PORT | Start Date `<input type="date">`, default today, on the form door. No end-date input anywhere in the app. (AI door derives its start — §2c.) |
| 5 | PORT | One "Submit Application" POST, antiforgery-protected; every failure re-renders the SAME Apply form with vehicles/types/campuses repopulated and errors on-screen. The frozen tree already proves this pattern in the garage and the Understood gate; the Apply form itself is new. |
| 6 | PORT | Zero vehicles → the form is replaced by an "Add a vehicle first" card + button to `/Vehicles/Create` (garage itself is item-verified under R2). |
| 7 | PORT | Zero Active permit types → form replaced by a "No permit types available" card. Reachable only if seed validation is bypassed; the card exists regardless. |
| 8 | PORT | Apply page carries a "What happens next" 3-step explainer card (Apply → Pay with PayPal (or free) → Permit live + PDF) and a trust note ("Check your college, car, and total before you pay — Paid comes only from PayPal's capture."). Fresh wording, Duvara's placement. |

### B. PermitType model
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 9 | HAVE | Frozen `PermitType` already matches field-for-field: Id, Code (Daily\|Term, EF text conversion), Name required ≤200, DurationDays, PriceCents, Active. Rebuild identically. |
| 10 | ADAPT | Two types (Daily, Term) seeded — HAVE via `HasData`. **Override of Duvara's config-priced seeding, with reason:** Duvara's prices come from its own config because it is one school; Solvia's price truth is the per-college published fee (§2a), so a config price would be a second, wrong price source. The portable substance — *startup refuses to run on bad pricing data* — is kept as startup validation: exactly Daily+Term present and Active, both DurationDays > 0, fee catalog loaded with ≥1 published Term price and ≥1 parseable day-pass price; any failure throws at startup with the failing check named. |
| 11 | ADAPT | Apply-time price fallback becomes: if the resolved fee is missing at Apply, re-read the fee catalog row (never the type row, never config); if it is still unpublished, block with an error naming the campus and the unpublished price. No write-back to the PermitType row — Solvia never writes prices onto types. |

### C. Apply POST rules
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 12 | ADAPT | Category resolution without logins — full design in §2b. Enum values Student/Employee/Public/Faculty/Visitor all exist; Faculty/Visitor price like Public; Employee resolves PriceCents = 0. The resolver input is the seeded fictional person's email domain + an override flag on the person row — never a login claim. |
| 13 | ADAPT | Daily portion PORT exactly: end = start (same day). Term portion is Solvia's term-calendar rule (§2c): end is always computed, never typed; the computed end comes from `TermDatesService` for the application's campus+term. |
| 14 | ADAPT | Application row creation is HAVE in substance (owner, vehicle, type, start/end, PriceCents snapshot, PaymentStatus NotRequired-if-0 else Pending, Status Pending, SubmittedAtUtc all exist). Delta: the row gains a **Category snapshot** (copied at Apply, like the price) and keeps Solvia's Campus + Source stamps. One creation brain (`ApplicationService.CreateAsync`) serves both doors. |
| 15 | ADAPT | The composite unique index (StudentId, VehicleId, PermitTypeId, StartDate, EndDate) is HAVE. Delta: both doors catch `DbUpdateException` from that index and surface Duvara's "already submitted…" message on the same screen instead of a 500. |
| 16 | PORT | Form-door validation messages with Duvara's meanings: no vehicle → "Please select a vehicle."; no type → "Please select a permit type."; not-owned vehicle or inactive type → general invalid-selection error; unpublished campus price → error naming the campus fee (Solvia's form of the pricing-config error). |
| 17 | ADAPT | Free outcome is HAVE in the money brain (`CheckoutService` confirms $0 as NotRequired with zero PayPal calls and mints number+token). Delta: the form door routes a $0 application straight through issuance at Apply POST and redirects to My Permits — the Checkout page is never rendered for it. |
| 18 | HAVE | Paid outcome redirects to `/Checkout/{id}` — the frozen Confirm → Checkout redirect is exactly this; the form door's POST returns the same redirect. |

### D. Checkout page
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 19 | ADAPT | Guards are only half-present today (Checkout `Index` 404s unknown ids but has no owner check and no Paid/$0 redirect). Redo: not found OR not the current demo person's → 404; PriceCents ≤ 0 or PaymentStatus Paid → redirect My Permits; the page never renders in those cases. |
| 20 | ADAPT | Summary rebuilt to Duvara's shape: tiles + detail list — type Name, plate + state, valid range start → end, amount due at 2 dp with currency (config `PayPal:Currency`, default USD). Campus and category join the detail list (Solvia axes). |
| 21 | PORT | Explainer copy on Checkout: students and the public pay the college's published fee; employees are exempt and never see this page for a $0 permit; trust box "Verify permit, vehicle, and amount before paying." |
| 22 | OUT | **Confirming the checklist default.** Stripe is not Solvia's path; PayPal Orders is the live and only processor (hackathon core requirement). Recorded so the checker never flags Stripe's absence. |

### E. Stripe flow
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 23–29 | OUT | **Confirming the checklist default.** No Stripe SDK, pages, webhook, or embedded view is built. Duvara's Stripe success/cancel *display-only* principle survives inside item 60's adaptation: Solvia's return/cancel endpoints render no success state either. |

### F. PayPal machinery
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 30 | ADAPT | `POST /api/paypal/orders` exists (PayPalController + CheckoutService). Deltas ported: (i) **idempotency** — a Pending application that already holds an order id returns that id, never a second order (today `StartAsync` mints a fresh order per call); (ii) Employee-category application → flipped to NotRequired + issued, response "not required"; (iii) already Paid → "not required", no order; (iv) payable application with PriceCents ≤ 0 → hard pricing error; (v) owner check against the demo person (403 wrong owner / 404 unknown). Antiforgery + demo-person requirement stand. |
| 31 | ADAPT | Capture-by-order-id is HAVE (`CaptureByOrderAsync` → the single `ApplyCaptureAsync`: only COMPLETED sets Paid + capture id + PaidAtUtc; buyer return alone changes nothing). Delta: the capture endpoint adds the same owner check as item 30 before capturing. |
| 32 | ADAPT | `PayPalService` is the only PayPal talker — HAVE for OAuth token, create, capture. Delta: it gains `VerifyWebhookSignatureAsync` (PayPal verify-webhook-signature call, webhook id from config) so item 33 has exactly one home for PayPal talk. |
| 33 | PORT | Inbound PayPal webhook, Duvara's rules, Solvia's improvement kept: `POST /api/paypal/webhook` (antiforgery-exempt, signature-verified via item 32; empty body / failed verify → 401; missing event id → 400). Idempotent via a new `WebhookEventLog` table (Provider + EventId unique). Only `PAYMENT.CAPTURE.COMPLETED` acts: find the application BY CAPTURE ID; amount ≠ PriceCents snapshot or Employee/$0 → mark Failed; else Paid + PaidAtUtc if unset, **and issue number+token if absent** (Duvara's gap; Solvia's HAVE behavior from `CheckoutService` is preserved by routing the webhook through the same issuance service). Other event types: logged, ignored. |

### G. Issuance
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 34 | ADAPT | **Confirming the checklist default.** Mint-at-issuance timing and the ≤10× uniqueness retry are HAVE (`PermitNumberService` + `CheckoutService.IssuePermitArtifactsAsync`, mint-if-absent). Format decision: Solvia's shipped `SOL-YYYY-NNNNNN` format **stands** (the checklist allows format-equivalence; the category code is NOT folded into the number because category is displayed as its own field on My/Verify/PDF, and the number format is already in the demo video script and judge-facing docs). Deltas: one `IssuanceService.IssueIfAbsentAsync` becomes the only mint path (called by free-at-Apply, capture, and webhook), and the application gains `ReceiptIssuedAtUtc`, stamped at mint. VerifyToken minting is part of the same step, as today. |
| 35 | ADAPT | **Confirming the checklist default.** No SMTP receipt email is built. The shipped Zapier payment-complete outbound webhook (`PaymentWebhookService`, fires after the Paid/free truth is saved, never blocks a payment, no student PII) is the receipt-out mechanism; its payload gains `receiptIssuedAtUtc` and the public `verifyUrl` so the Zap's receipt email + ledger row carry the permit link. |

### H. My Permits
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 36 | ADAPT | Scope (own applications only) and the bottom "Get another permit" link are HAVE. Deltas: ordering pinned newest-first by SubmittedAtUtc, and the header carries the "Apply for a Permit" button (today it sits only in the empty state / page bottom). |
| 37 | PORT | Summary tiles on My Permits: total permits / count Paid / count pending payment (PriceCents > 0 and not Paid). Counts computed from the person's own rows only. |
| 38 | ADAPT | Table/cards rebuilt to Duvara's columns: Submitted (Pacific time), Permit (type Name), Vehicle (plate + state), Dates (start → end), Category, Payment (status word with "Not Required" spelled out, amount in parentheses when > 0). Mobile card layout with success/warning badges is HAVE in spirit (tonight's big-text cards) and is kept; the column set and the Category column are the deltas. |
| 39 | ADAPT | "Pay Now" row action: shown only when PriceCents > 0 and not Paid → `/Checkout/{id}`, `target="_blank"` (tonight's "Finish paying" link exists without the new-tab behavior and without the price condition being explicit). |
| 40 | ADAPT | "Permit (PDF)" row action: shown only when Paid or NotRequired → the Receipt/PDF action. The gate itself is HAVE (PermitPdf serves only live permits); the row action on My is the delta. |
| 41 | ADAPT | Empty state keeps tonight's "No permits yet" + big Apply button; wording aligned to "No applications yet" with the Apply link — one line, verified by the checker, not assumed. |
| 42 | HAVE | No search/filter/sort/pagination on My Permits — true today; the redo builds none and the checker confirms the absence. |

### I. Receipt PDF
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 43 | ADAPT | **Confirming the checklist default.** No roles exist in the demo: the owner gate stands (a non-owner demo person gets **403**), unknown id → **404**, and a known, owned, not-yet-live application → **400** with a plain-language "only after payment" body. Delta from tonight: `PermitPdf` currently redirects unpaid applications to the Receipt page; the redo aligns the status codes to Duvara's gate while the Receipt *page* keeps its friendly redirect behavior. |
| 44 | ADAPT | **Confirming the checklist default.** QuestPDF, Letter — tonight's `PermitPdfService` already carries title/org/rule, summary box (permit # or "(pending)", issued timestamp, payment line, amount), details table (type, vehicle plate+state, dates, Paid-at UTC, **PayPal order/capture ids** ✔ HAVE), and the verify section (QR-equivalent: public `/verify?t=` URL + token block ✔ shipped equivalent). Deltas: the payment line reads **EXEMPT (Employee)** for Employee-category NotRequired (other NotRequired reads FREE — ISSUED with the campus $0 fee named), a **privilege line** (Staff Parking Eligible vs Student/Public Parking Eligible) joins the summary box, Category joins the details table, and the verify section heading becomes "Proof of Parking (For Enforcement)". |
| 45 | HAVE | **Overriding toward the checklist's own Solvia note:** regeneration on every call is the shipped and accepted behavior. No PDF cache fields are added — a cache would be a second truth that can go stale; the checker verifies two consecutive calls both return a fresh 200 %PDF. |

### J. Public verification + validity
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 46 | ADAPT | `/verify?t=` page is HAVE (anonymous, unknown/missing token → neutral page, no other permits' data). Deltas PORTed onto it: type, plate state, category, privilege line, payment status word, last-updated timestamp, and "(not issued)" where a token exists without a number (defensive — tokens mint with numbers today). Rate-limit decision: **yes, lightweight** — an in-memory fixed-window limiter (60 requests/minute/IP) on `/verify` and its JSON twin, registered via a one-line Program.cs change request to R0. |
| 47 | HAVE | `PermitValidity.Check` already implements exactly: active iff start ≤ day ≤ end AND Status ≠ Denied AND PaymentStatus ∈ {Paid, NotRequired}. Rebuild identically; T5's roster/verify split (roster truth vs date-window truth) is preserved. |
| 48 | PORT | `GET /api/verify?t=` — JSON twin of the verify page: same token, same `PermitValidity` verdict, same field set as item 46, no extra data. |

### K. Statuses + lifecycle
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 49 | ADAPT | PaymentStatus values NotRequired/Pending/Paid/Failed/Refunded/Expired are HAVE. Delta: **PartiallyRefunded is added** to the enum (stored as text, no migration framework — EnsureCreated world) so the value set matches Duvara's exactly. Exercised transitions stay Solvia's: Pending→Paid (capture/webhook), Pending→Failed (non-COMPLETED capture, webhook mismatch), Pending→NotRequired (Employee / $0 at Apply or create-order), NotRequired at creation ($0). Refund values remain unexercised in the demo, as in Duvara's permit module. |
| 50 | HAVE | ApplicationStatus Pending/Approved/Denied, created Pending, no admin approve/deny action, validity treats Pending-as-counts-when-paid — all already Solvia's design (`PermitValidity`). Confirmed, nothing built. |
| 51 | PORT | Optimistic concurrency: `PermitApplication` gains a `ConcurrencyToken` (Guid, EF `IsConcurrencyToken()`); every service that saves an application (ApplicationService, CheckoutService, webhook handler, price repair) sets a fresh token value before save. A stale write throws `DbUpdateConcurrencyException`, which the doors translate to Duvara's same-screen error style, never a silent overwrite. (Guid token chosen over a rowversion column because SQLite has no native rowversion; the portable substance is the enforced check.) |

### L. Admin / back-office orbit
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 52 | HAVE | **Confirming the checklist default.** No permit-type management UI exists or is built; types live in seed/config exactly as tonight. The item 7 empty-types card wording ("in the database") is the only admin-shaped surface, as in Duvara. |
| 53 | ADAPT | **Confirming the checklist default.** College Roster header gains Duvara's three admin tiles, computed per campus: total permits (issued: Paid + NotRequired), permit revenue (sum of Paid PriceCents snapshots), expired-permit count (EndDate < today and issued). |
| 54 | ADAPT | **Confirming the checklist default, sized:** the roster gains a "By student" section — one row per seeded student: permit count at this campus, their issued receipt numbers, sorted by permit count descending. That is the whole port; no separate per-user intelligence pages. |
| 55 | ADAPT | Minimal Solvia-shaped repair utility (not OUT — the piece is small and it protects the fee-truth story): `PriceRepairService.RepairPendingAsync()` recomputes PriceCents **from the fee catalog** (campus + type + term) for **Pending applications only** — Paid/NotRequired snapshots are historical truth and are never rewritten — and returns a change list (application id, old, new). Exposed as `POST /College/RepairPrices` with the result shown as a banner list on the roster. Null-safe: an unpublished fee leaves the row untouched and reports it as skipped. |
| 56 | OUT | **Confirming the checklist default.** Citations/enforcement are a different Duvara module. No officer lookup by permit number, no vehicle lookup console, no PermitStatusSnapshot. Solvia's public `/verify` (item 46) is the enforcement-facing surface. |

### M. Explicitly absent in Duvara (checker: do not demand these)
| # | Disp. | What it means concretely in the redo |
|---|---|---|
| 57 | HAVE | The Duvara **form** door references the vehicle by id only — no vehicle fields on Apply. True in the redo: vehicle CRUD stays in the garage (R2). The AI door's free-text car sentence is Solvia's separate front door, not the Duvara form; this item judges the form door. |
| 58 | HAVE | No zones/lots/waitlist/renewal/transfer/cancellation/refund-action for applications — absent tonight, absent in the redo; the checker greps the route table to keep it so. |
| 59 | ADAPT | No *application* confirmation page: both doors redirect after Apply POST (free → My, paid → Checkout) with status banners only — ported. Solvia's post-payment Receipt/Done pages are payment receipts, not application confirmations, and stay (they are items 44/20's surfaces). The checker judges the Apply POST's outcome, not the receipt's existence. |
| 60 | ADAPT | No PayPal return/cancel **landing pages**: the redo's checkout uses the in-page JS-SDK flow (items 30/31) as the buyer path. The redirect flow's `Return`/`Cancel` endpoints are kept only as capture-or-release plumbing that immediately redirects to Checkout/Done with a banner — they render no page and can never show a Paid state (capture truth decides, as tonight). The checker hits `Return` with a bogus token and verifies no success state renders. |

---

## 2. The five design answers

### (a) PermitType: real table, Duvara's shape — but price truth stays with the college
`PermitType` stays a **real EF table**, exactly Duvara's row (item 9,
HAVE): `Id, Code (Daily|Term as text), Name, DurationDays, PriceCents,
Active`, two rows seeded at startup. The mapping onto Solvia's fee truth
is a strict division of labor:

- **The type row answers "what kind of permit":** Code, display Name,
  Active, and DurationDays (Daily = 1; Term = 120, the seeded fallback
  duration the term calendar reads today).
- **The type row never answers "what does it cost."** Its PriceCents is
  seeded 0 and is formally a *non-authoritative placeholder*: no code
  path may charge from it, and R1 ships a guard test that prices Fresno
  Term at 3000 from the catalog while the Term type row sits at 0.
- **Price resolution is one function**, owned by a new
  `PricingService.ResolvePriceCents(campus, typeCode, term)`:
  Term → `IFeeCatalog.SemesterPriceCents(fee, term)` (FREE campus = 0,
  a real price; unpublished = null → blocked, item 11); Daily → the
  campus's parsed day-pass price (`CampusFee` gains a parsed
  `DayPassCents`, null when unpublished). Employee category overrides
  the result to 0 *after* resolution (§2b) — the catalog is still
  consulted first, so an unpublished fee blocks even an employee
  application rather than silently issuing.
- **DurationDays vs the term calendar:** `TermDatesService` remains the
  authority for Term windows (it already derives PermitEndDate from the
  Term type's DurationDays). Daily never consults it: end = start.

### (b) Category without logins: a property of the fictional person, resolved by rule, snapped at Apply
Solvia has no login stack and gets none. Category is resolved from the
**seeded fictional person** the demo switcher selects, by a pure
`UserCategoryResolver`:

1. `Student.CategoryOverride` (new nullable column) wins if set
   (Duvara's per-user admin override flag; seeded null for everyone —
   the field exists, no UI edits it).
2. Else the person's Email domain is matched against config lists
   (`Category:EmployeeDomains`, `Category:StudentDomains`).
3. Else **Public**.

The seeded cast grows by two fictional people so every branch is
demoable honestly: Maria/Diego/Priya/Sam stay **Student**
(`@student.example`); **Elena** (`elena@college.example`, in
EmployeeDomains) is **Employee**; **Pat** (`pat@public.example`) is
**Public**. Faculty/Visitor exist as enum values (priced like Public)
with no seeded person. The demo switcher and the Apply/Checkout/My/
Verify surfaces all *label* the category in plain words ("Demo person:
Elena — Employee (fictional)"), so nothing pretends an authentication
happened. The resolved category is **snapshotted onto the application
at creation** (item 14), and pricing reads the snapshot:
Employee → 0 → NotRequired + immediate issuance; everyone else → the
§2a fee. The roster and PDF read the snapshot, never a re-resolution.

### (c) Start Date and the two doors: one application model, one creation brain, two front doors
Both doors create the **same** `PermitApplication` through
`ApplicationService.CreateAsync`, whose input is one normalized record:
`{ StudentId, VehicleId, Campus, PermitTypeCode, TermKind?, StartDate,
Source }` — Source ∈ {`form`, `ollama`, `fallback`} (the AI door keeps
tonight's honest source label; the form door stamps `form`). Category,
price snapshot, end date, and duplicate handling happen **inside**
CreateAsync, identically for both doors:

- **Form door (Duvara's):** the student picks Campus, Vehicle, Type,
  Term (shown when Type = Term), and Start Date (default today).
  End: Daily → StartDate; Term → `TermDatesService` window for
  (Campus, Term, StartDate.Year), with End = that window's
  PermitEndDate; a StartDate outside the chosen term's window is a
  same-form validation error naming the window ("Fall runs Aug 24 –
  Dec 11 — pick a date inside it or change the term").
  POST outcome is Duvara's: free → issued + My Permits; paid →
  `/Checkout/{id}`. No Review step on this door.
- **AI door (Solvia's):** 3 questions only (school / car / term) — no
  Start Date is ever asked, preserving the product promise. The
  extractor's Term + chosen type feed CreateAsync with
  StartDate = the term window's TermStart (Term) or today (Daily);
  the car sentence is resolved/created as a vehicle first (tonight's
  SaveDraft behavior), then the same CreateAsync runs at the
  Understood gate, with the Review screen kept as the AI door's
  trust moment before Confirm → `/Checkout/{id}`.

One model, one price rule, one duplicate rule, one issuance rule —
the doors differ only in how the inputs are gathered.

### (d) Issuance timing: mint exactly at the live transitions, through one service
Solvia's HAVE behavior stands and becomes the stated law. Permits are
issued **only** inside `IssuanceService.IssueIfAbsentAsync`, which
mints (if absent) VerifyToken + ReceiptNumber (`SOL-YYYY-NNNNNN`,
≤10 uniqueness retries against the DB + unique indexes) and stamps
`ReceiptIssuedAtUtc`. It has exactly three call sites, all at the
moment an application becomes live:

1. **Free at Apply** — CreateAsync resolves PriceCents = 0 (FREE
   campus or Employee category) → PaymentStatus = NotRequired → issue
   → redirect My Permits.
2. **PayPal capture** — `CheckoutService.ApplyCaptureAsync` on a
   COMPLETED capture → Paid → issue → outbound Zapier webhook fires
   after the save (item 35).
3. **PayPal webhook** — item 33's handler on a verified
   CAPTURE.COMPLETED that finds the application not yet Paid → Paid →
   issue (Duvara's gap, closed the Solvia way).

No other path mints: not the return page, not the receipt page, not
the PDF. A second call on an already-issued application is a no-op
(`??=` semantics, as tonight), so capture + webhook racing is safe.

### (e) Tonight's shipped extras: they stay — Solvia's hackathon layer around the Duvara core
None of these are Duvara pieces and none are cut; they live **outside**
the permit core and touch it only through its contracts:

| Extra | Where it lives in the redo |
|---|---|
| AG Grid Community fee table (+ native fallback, same seam) | `Views/Shared/_FeeTable.cshtml` + `wwwroot/js/feetable*.js`, `FeeTable:Renderer` flag; renders on College/Fees and feeds the AI door's campus pick. Consumes `IFeeCatalog` only. |
| Elastic search seam | `Services/{ICollegeSearch, BuiltinCollegeSearch, ElasticCollegeSearch}` + `scripts/elastic/`; built-in is default, Elastic is config-selected with fallback, provider labeled on-screen. Feeds campus pick lists; never prices. (ELSER still not claimed.) |
| Zapier outbound webhook | `Services/PaymentWebhookService` + `Webhook:Url`; notified by CheckoutService/issuance after a live transition (item 35). Never blocks a payment. |
| Postman collection | `postman/` — create → capture → verify → extract against the redo's endpoints, honest 503 shape asserted. |
| KERNEL fee-refresh tool | `tools/fee-refresh/` — offline ops against `data/fees.csv` (the trimmed 10-campus file, SHA `327aca14…`); no app dependency. |
| Sponsor docs + honesty docs | `docs/SPONSORS.md` (source of truth for sponsor claims; Bryntum stays OUT — no free edition; Render stays "not deployed"), `docs/apimatic-context.md`, `docs/zapier-recipe.md`, `docs/PRIVACY.md`. |
| AI layer (Phi-4-mini via Ollama + deterministic fallback) | The AI door (R3 + R4). 100% local, Source labeled, extraction has no price field by construction — the catalog prices, never the model. |
| QuestPDF permit PDF, vehicle garage + catalog, design theme (navy/PayPal-blue/gold), demo switcher | R5, R2, R0 respectively — rebuilt fresh, behavior pinned by the frozen tree's proven versions. |

---

## 3. Track slice — R0…R10

House rules: **no two tracks own the same file**; a track needing a
file it doesn't own files a one-line change request to the owner
(Program.cs rule from BUILD-PLAN §2 stands — R0 owns it). Every track's
done-test is in house style: `dotnet build --no-incremental` 0
warnings / 0 errors + suite green (in-process xUnit runner per the
Lyceum) + a live HTTP walk on a fresh file DB with forms replayed from
their own hidden fields. All paths below are inside
`~/workspace/paypal-hackathon/solvia-redo/` unless marked repo-root.

### R0 — Foundation, config, shell
- **Owns:** `src/Solvia.slnx`, `src/Solvia/Solvia.csproj`, `Program.cs`,
  `Config/**` (existing options + new `CategoryOptions`,
  `PayPal:Currency`, `PayPal:WebhookId`), `appsettings*.json`,
  `Properties/**`, `.env.example` (repo root), `NOTICES.md` (stub; R10
  completes), `assets/logo.webp` + `data/fees.csv` (repo root — data
  carried over byte-identical, fees SHA `327aca14…`),
  `Controllers/{HomeController, DemoControllerBase}.cs`,
  `Views/Home/**`, `Views/Shared/{_Layout.cshtml, _Layout.cshtml.css,
  Error.cshtml}`, `Views/_View*.cshtml`, `wwwroot/css/**` (theme:
  navy/PayPal-blue/gold, big-text).
- **Depends on:** nothing.
- **Done =** app runs from the redo folder and serves Home 200 with the
  demo switcher listing all six fictional people; `git grep`-style
  secret sweep over the redo tree returns placeholders only; fees.csv
  hash matches the frozen file.

### R1 — Domain core, fee truth, pricing, category
- **Owns:** `Models/**` **except** `Models/VehicleCatalog.cs` (R2) —
  includes `PermitApplication` (+ Category snapshot,
  ReceiptIssuedAtUtc, ConcurrencyToken), `Student` (+ CategoryOverride),
  `PermitType`, `Vehicle`, `Models/Enums/**` (+ `UserCategory`,
  PaymentStatus + PartiallyRefunded), `Models/WebhookEventLog.cs`
  (pre-built for R7), `Data/SolviaDbContext.cs` (conversions, indexes,
  seeds incl. Elena/Pat, startup validation hook),
  `Services/{IFeeCatalog, FeeCatalogService, CampusFee}.cs`
  (CampusFee gains parsed DayPassCents),
  `Services/{ITermDatesService, TermDatesService, TermDates}.cs`,
  `Services/{PricingService, UserCategoryResolver}.cs`,
  `tests/Solvia.Tests/{DomainTests, PricingTests, FeeCatalogTests}.cs`
  (+ the tests project file).
- **Depends on:** R0 (solution + config shapes).
- **Done =** suite pins: Fresno Term/Fall = 3000 and YCCD = 0 from the
  catalog while the Term type row's PriceCents = 0 (price never from
  the type); a clean day-pass parse (CSU Bakersfield "$6/day" → 600)
  and null for an unpublished day pass; resolver cases (override >
  employee domain > student domain > Public; Faculty/Visitor priced as
  Public); startup validation throws naming the failed check when a
  type is deactivated in a test context; duplicate insert violates the
  unique index; concurrency token changes on save.

### R2 — Vehicle garage (Duvara's CRUD orbit)
- **Owns:** `Controllers/VehiclesController.cs`, `Views/Vehicles/**`,
  `Models/VehicleCatalog.cs`, the vehicle portions of
  `Models/ViewModels/FlowViewModels.cs` **as a separate file**
  `Models/ViewModels/VehicleViewModels.cs` (R2 owns it; R4 owns
  FlowViewModels.cs proper), `tests/Solvia.Tests/VehicleGarageTests.cs`.
- **Depends on:** R1 (Vehicle model).
- **Done =** HTTP walk: Create persists with server-stamped owner;
  blank plate saves nothing and says so; off-catalog make/model persist
  as typed (cascade has an "Other — type it" escape); Edit re-offers
  existing values; a vehicle owned by another demo person 404s on Edit.

### R3 — AI extraction (Ollama + fallback)
- **Owns:** `Services/{IAiExtractor, OllamaExtractionService,
  FallbackExtractor}.cs`, `Controllers/ChatController.cs`,
  `tests/Solvia.Tests/ExtractionTests.cs`.
- **Depends on:** R1 (`IFeeCatalog` — the catalog is the only college
  source an extraction may name).
- **Done =** the BUILD-PLAN sentence ("…Fresno City College… blue 2019
  Honda Civic, plate 8XYZ123… fall term") returns Fresno + Fall +
  plate via handler-level tests with `Source=ollama`, and the same
  fields with Ollama absent and `Source=fallback`; spaced plate
  "8XYZ 123" normalizes; the result type carries **no price field**
  (compile-level fact the checker greps).

### R4 — Application core + the two doors
- **Owns:** `Services/{ApplicationService, IssuanceService,
  PermitNumberService}.cs` (IssuanceService exposes
  `IssueIfAbsentAsync`, consumed by R6/R7),
  `Controllers/PermitsController.cs` (Apply form door + Ask/Questions/
  Understood/Review/Confirm AI door + SwitchType),
  `Views/Permits/{Apply, Ask, Questions, Understood, Review}.cshtml`,
  `Models/ViewModels/FlowViewModels.cs`,
  `tests/Solvia.Tests/{ApplicationTests, FlowTests}.cs`.
- **Depends on:** R1 (pricing/category/domain), R2 (garage for the
  form door's vehicles), R3 (extractor interface for the AI door).
- **Done =** live walks on a fresh DB: (i) form door — Maria + her
  saved vehicle + Fresno City + Term + start today → POST → redirect
  `/Checkout/{id}`; Elena (Employee) same campus → issued at POST,
  lands on My with a permit number, Checkout never rendered; zero-
  vehicle person → item 6 card; duplicate submit → same form +
  "already submitted…" ; start outside the term window → same-form
  error naming the window. (ii) AI door — "blue Honda Civic" (no
  plate) stops at the Understood gate, typed plate advances, Review
  shows $30.00, Confirm posts id only → Checkout 200. Suite green.

### R5 — My Permits, Receipt page, Permit PDF
- **Owns:** `Controllers/MyPermitsController.cs` (attribute routes
  under `/Permits`: My, Receipt, PermitPdf — a second controller file
  so R4's PermitsController stays single-owner),
  `Views/Permits/{My, Receipt}.cshtml`, `Services/PermitPdfService.cs`,
  `tests/Solvia.Tests/{MyPermitsTests, PermitPdfTests}.cs`.
- **Depends on:** R4 (applications + issuance), R1.
- **Done =** My shows tiles with counts matching a seeded scenario
  (total / Paid / pending-payment), Duvara's column set incl.
  Category and "Not Required" spelled out, Pay Now only on
  price > 0 + unpaid rows and opening Checkout in a new tab, PDF
  action only on live rows; PDF gates proven over HTTP (non-owner
  403, unknown 404, owned-unpaid 400 plain text, live 200 %PDF);
  `pdftotext` on a live permit shows permit number, plate + state,
  PayPal ids, category, privilege line, and EXEMPT (Employee) on an
  Elena permit; two consecutive PDF calls both 200 (regeneration).

### R6 — PayPal + Checkout (the money brain)
- **Owns:** `Services/{IPayPalService, PayPalService, CheckoutService}.cs`
  (PayPalService includes `VerifyWebhookSignatureAsync` for R7),
  `Controllers/{CheckoutController, PayPalController}.cs`,
  `Views/Permits/Checkout.cshtml`, `Views/Checkout/Done.cshtml`,
  `tests/Solvia.Tests/{PayPalServiceTests, CheckoutTests}.cs`.
- **Depends on:** R4 (`IIssuanceService` — concretely
  `IssuanceService.IssueIfAbsentAsync`), R1.
- **Done =** mocked-handler suite: create-order is idempotent (second
  create on a Pending application returns the stored order id; the
  fake PayPal handler saw exactly one create call); Employee create →
  NotRequired + issued, no order; already-Paid create → "not
  required"; capture COMPLETED is the only path to Paid (non-COMPLETED
  → Failed, no issuance); Checkout guards over HTTP (unknown 404,
  other person's 404, Paid → redirect My, $0 → redirect My);
  `Return` with a bogus token renders no success state.

### R7 — Webhooks (in + out) and Verify API
- **Owns:** `Services/{PaymentWebhookService, PayPalWebhookHandler}.cs`,
  `Controllers/{PayPalWebhookController, VerifyApiController,
  VerifyController}.cs`, `Views/Verify/**`,
  `tests/Solvia.Tests/{WebhookTests, VerifyTests}.cs`.
  (VerifyController/views move here from the old T5 shape — the public
  verification surface is one owner's job; the rate limiter is wired
  via a one-line Program.cs change request to R0.)
- **Depends on:** R6 (signature verification + capture truth), R4
  (issuance), R1 (WebhookEventLog entity, PermitValidity).
- **Done =** webhook suite: bad signature → 401 and nothing saved;
  the same event id delivered twice → second delivery is a logged
  no-op (event log shows one acted, one duplicate); CAPTURE.COMPLETED
  with a mismatched amount → Failed + no issuance; a correct event on
  an unpaid application → Paid + issued exactly once. Outbound:
  payload for a live permit contains permitNumber, verifyUrl,
  receiptIssuedAtUtc and **no** student name/email/plate (test greps
  the serialized JSON). `/api/verify?t=` returns the same verdict and
  fields as the page for the same token; the page shows type,
  category, privilege line, status word, last-updated; burst test
  trips the rate limiter.

### R8 — College side: roster, fee table, search seam, price repair
- **Owns:** `Controllers/CollegeController.cs`, `Views/College/**`,
  `Views/Shared/_FeeTable.cshtml`, `wwwroot/js/{feetable, calendar}*`,
  `Services/{ICollegeSearch, BuiltinCollegeSearch,
  ElasticCollegeSearch, PermitValidity, PriceRepairService}.cs`,
  `tests/Solvia.Tests/{RosterVerifyTests, CollegeSearchTests,
  PriceRepairTests}.cs`.
- **Depends on:** R1 (catalog + validity), R6 (Paid data shapes).
- **Done =** roster for Fresno shows the three tiles with sums that
  match a flow-produced scenario (Maria Paid / Diego Pending / Priya
  Free-Issued / Sam NoPermit — produced through the real services,
  never hand-inserted rows, per the Lyceum); "By student" section
  sorted by permit count with receipt numbers; fee table renders
  identical values under `native` and `aggrid`; search page labels
  which provider served; price repair changes a tampered Pending
  price back to the catalog value, reports it, and provably leaves a
  Paid snapshot untouched.

### R9 — Sponsor artifacts + docs (no app code)
- **Owns:** `postman/**`, `scripts/elastic/**`, `tools/fee-refresh/**`,
  `docs/{SPONSORS, apimatic-context, zapier-recipe, PRIVACY, USE-CASE,
  SCHEMA}.md`, `render.yaml` (kept as an artifact; README/SPONSORS
  keep the honest "not deployed" line).
- **Depends on:** R6/R7 endpoint shapes frozen.
- **Done =** Postman collection parses and its automated steps pass
  against the local redo app up to the manual sandbox-approval click;
  fee-refresh runs against 3 campuses → UNCHANGED, fees.csv hash
  unchanged; docs grep sweep: no "25 colleges" claim, no Bryntum
  shipped claim, QuestPDF license stated as Community (not MIT),
  SCHEMA.md matches the redo model field-for-field (incl. Category,
  ReceiptIssuedAtUtc, ConcurrencyToken, WebhookEventLog).

### R10 — Clean-install, independent checker, swap
- **Owns:** `docs/{CLEAN-INSTALL-LOG, CHECK-REPORT}.md` (redo
  editions), `NOTICES.md` (final), the README's final wording.
  **R10 is never a builder** — findings are verdicts filed to the
  owning track.
- **Depends on:** R0–R9.
- **Done =** (i) clean-install from `solvia-redo/` following ONLY the
  README: build 0/0, Priya's YCCD free path reaches a VALID `/verify`
  with no PayPal keys and no Ollama (fallbacks proven, not assumed);
  (ii) `CHECK-REPORT` signs **all 60 checklist items** one by one
  against the redo tree (any FAIL blocks the swap); (iii) the swap
  (§4) executes and its read-back verification passes.

**Build order:** R0 → R1 → (R2, R3 in parallel) → R4 → (R5, R6 in
parallel) → R7 → R8 → R9 → R10. Tracks in the same wave never share a
file by construction above.

---

## 4. Swap protocol — how the redo replaces `src/` in the repo

1. **Freeze point.** Repo `jlhpacheco/solvia` HEAD `595ea23e` is the
   freeze. From plan adoption, the frozen trees are read-only; any
   hotfix to the live repo during the redo needs Jose Luis's explicit
   word and, if it lands, R10 re-bases the swap parent onto the new
   HEAD and the checker re-signs the affected items.
2. **Gate.** No swap until R10's done-test passes in full: suite
   green, clean-install log clean, and all 60 items signed PASS in
   the redo CHECK-REPORT. One FAIL = no swap, no exceptions.
3. **The push — one motion, full-tree replace.** A single git-data
   push whose **parent is the freeze HEAD** (`595ea23e`, or the
   re-based HEAD per step 1) and whose tree is built **from the redo
   manifest alone** (no `base_tree` inheritance — so files the redo
   deliberately drops actually disappear; bin/obj/.db and other build
   junk excluded by the manifest, per the Lyceum's raced-push
   lesson). One commit, message naming the redo and the checklist.
   The same tree carries, in the same motion:
   - the new `src/` (and `tests/`, `postman/`, `scripts/`, `tools/`),
   - **README** updated to the redo (run steps re-verified by R10),
   - a new **`docs/ARCHITECTURE.md`** stating plainly: *"Solvia's
     permit core — application form, permit-type catalog, issuance,
     receipt, verification — follows the design of CarPermitApp, the
     author's own earlier permit application. The AI extraction
     (Phi-4-mini, local), the published-fee research catalog, and the
     PayPal Orders integration were built for this hackathon."*
   - **`docs/REDO-PLAN.md`** — a copy of this plan, as the build
     record.
4. **Docs disposition (decided, not deferred):**
   - `docs/CHECK-REPORT.md` — **updated by addendum**: the T10 report
     stays as dated history; a "REDO re-verification" section appends
     the 60-item sign-off against the swap commit.
   - `docs/CLEAN-INSTALL-LOG.md` — **updated by addendum**: the new
     dated clean-install run appends; the original log stands.
   - `solvia-prep/BUILD-PLAN.md` — **archived**: its header gains
     "SUPERSEDED by REDO-PLAN.md for the permit core; Solvia-layer
     decisions not overridden there still stand." It is a local file
     (the repo never carried it); nothing in the repo changes for it.
   - Devpost draft 1245990: the story's architecture sentence is
     updated to match `docs/ARCHITECTURE.md` in the same work session
     as the swap — **draft only**, by the coordinator via the saved
     Devpost process; the final submit still waits for Jose Luis's
     explicit word.
5. **Read-back verification (the swap is not done until this
   passes):** Contents-API read-back of the new HEAD — parent equals
   the freeze HEAD; `data/fees.csv` SHA-256 = `327aca14…`; blob spot
   checks byte-identical for `Program.cs`,
   `Models/PermitApplication.cs`, `README.md`,
   `docs/ARCHITECTURE.md`; tree file count equals the redo manifest
   count; the deleted-in-redo file list is confirmed absent.
6. **Rollback.** The freeze commit stays the swap's parent, so a
   rollback is one ordinary git-data push restoring the freeze tree —
   never a force-push, never a history rewrite. The push script used
   for the swap is kept in `solvia-prep/` with its manifest, so the
   rollback (and any re-swap) is reproducible.
7. **After the swap:** Jose Luis's PC test is a fresh `git pull` +
   delete the old `solvia.db` (new columns — Category,
   ReceiptIssuedAtUtc, ConcurrencyToken, WebhookEventLog — need
   EnsureCreated to build the new schema) + the standing key steps +
   the test script. Demo/testing access stays free and live through
   judging (Dec 15, 2026), as before.

---

## 5. Standing rules the redo inherits (not re-litigated)

- Country rule (final): China out, Russia out — only those two.
  Phi-4-mini (Microsoft, MIT) via Ollama, 100% local; no student data
  trains any model or leaves the machine.
- PayPal sandbox only; Paid comes only from a COMPLETED capture (or a
  verified webhook acting on one) — never from a return page.
- Secrets only via environment variables / user-secrets; a real
  secret in the tree fails R10 on sight.
- Maria's college stays a published-price campus (Fresno City, $30);
  the data set stays the trimmed 10 campuses; the research file with
  all 25 stays untouched in `research/`.
- Submission target stays Nov 11, 2026; final Devpost submit only on
  Jose Luis's explicit word. Voz Navega's Oct 23 deadline is not
  cannibalized by this redo.
