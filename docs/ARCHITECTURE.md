# ARCHITECTURE — how Solvia is put together

Solvia is an independent application first built during this hackathon.
Its permit core, vehicle extraction adapter, published-fee catalog,
PayPal Orders integration, issuance, receipts, and verification are
described below. General structural references do not define its
business model or payment implementation.

This file is the map of the code in this repo: what each piece is, and
which rules it owns. `docs/REDO-PLAN.md` is the build record (the
60-item checklist dispositions and the track-by-track plan this tree
was built against); `docs/CHECK-REPORT.md` is a historical review snapshot,
not verification of the current optional model providers. Current test
boundaries and live-provider opt-ins are in [TESTING.md](TESTING.md).

## One application model, two front doors

Both ways in create the **same** `PermitApplication` through one
creation brain, `ApplicationService.CreateAsync`
(`src/Solvia/Services/ApplicationService.cs`). Category resolution,
price snapshot, end-date computation, and duplicate handling live in
that one method, identically for both doors — the doors differ only in
how the inputs are gathered.

- **The form door** — `GET/POST /Permits/Apply` (Duvara's shape). The
  student picks Campus, one of their saved Vehicles, Permit Type,
  Term (when the type is Term), and a Start Date (default today).
  A free application is issued at the POST and lands on My Permits;
  a payable one redirects to `/Checkout/{id}`. There is no Review
  step on this door.
- **The AI door** — `/Permits/Ask` (Solvia's). The configured model
  drafts vehicle details from the car sentence: Ollama/Phi-4-mini by
  default, or the optional hosted Meta Standard `muse-spark-1.3` provider.
  College, permit type, and term remain UI selections. Both adapters use
  the same structured extraction contract and plate safeguards. The
  extraction result type has **no price field**; server pricing alone
  determines the amount. The person confirms the vehicle details on an
  editable review screen before the application is saved. An unavailable
  provider or unusable answer invokes the deterministic reader, with an
  honest `Source` label: `ollama`, `meta`, or `fallback`.

`AI_PROVIDER` selects `ollama` (default) or `meta`. The optional Meta adapter
calls the fixed `https://api.meta.ai/v1/chat/completions` endpoint with a
server-only API key, strict structured output, redirects disabled, and
authorization-header redaction. `META_MODEL` selects a supported Standard
model; `META_TIMEOUT_SECONDS` sets the total request budget (10 seconds by
default, clamped to 1–90). Ollama has its own bounded request budget. Input
is limited before either model is called; human-reviewed make/model fields
are limited to 50 characters, and state/color must be valid named values.
The model cannot create orders or mark a payment Paid.

For Development setup, `scripts/Set-MetaCredentials.ps1` prompts for the key
without echoing it and writes it through stdin to .NET user-secrets; these
are per-user development settings, not an encrypted vault. See the
[README](../README.md) for setup and [privacy](PRIVACY.md) for hosted data
processing and the adult-only Meta demo boundary. No live Meta request has
been verified. A direct warm Phi-4-mini CPU response took 32.26 seconds;
the application's 10-second budget fell back, so that direct response is
not proof of model inference through the app.

## Price truth: the college's published fee

A permit's price never comes from the permit-type row and never from
the AI. `PricingService.ResolvePriceCents` is the one pricing
function, and it reads the published-fee catalog (`data/fees.csv`,
served by `IFeeCatalog`): Term permits price from the campus's
published semester fee, Daily permits from its parsed day-pass
price. A campus whose price is not published blocks the application
with an error naming the campus — for every category, employees
included. The `PermitType` table keeps Duvara's shape
(`Daily`/`Term` seeded and Active), but its `PriceCents` is a seeded-0,
non-authoritative placeholder; no code path charges from it. Startup
validation refuses to boot on bad pricing data: both types present
and Active, durations positive, and the fee catalog loaded with at
least one published term price and one parseable day-pass price.

## Who pays: category without logins

Solvia has no login stack. The demo switcher picks one of six seeded
fictional people, and `UserCategoryResolver` resolves that person's
category by rule: an explicit override flag on the person row wins,
then email-domain lists from config, then Public. Maria, Diego,
Priya, and Sam are Students; Elena (`@college.example`) is an
Employee; Pat (`@public.example`) is Public. Faculty and Visitor
exist as enum values and price like Public. The resolved category is
**snapshotted onto the application at creation**, next to the price
snapshot, and every later surface (My Permits, roster, receipt, PDF,
verify) reads the snapshot. Employee resolves to $0 →
`NotRequired` → issued immediately; the Checkout page never renders
for a $0 permit.

Dates follow the same one-rule pattern: a Daily permit ends the day
it starts; a Term permit's end comes from `TermDatesService` for the
application's campus and term, and a start date outside the chosen
term's window is a same-form error that names the window.

## Issuance: minted at exactly three moments

A permit number (`SOL-YYYY-NNNNNN`), its public verify token, and
its `ReceiptIssuedAtUtc` stamp are minted only inside
`IssuanceService.IssueIfAbsentAsync`, which has exactly three call
sites — the moments an application becomes live:

1. **Free at Apply** — price resolves to $0 (a FREE campus, or the
   Employee category).
2. **PayPal capture** — a capture that returns COMPLETED.
3. **PayPal webhook** — a signature-verified
   `PAYMENT.CAPTURE.COMPLETED` that finds the application not yet
   Paid and issues through the shared Solvia service.

A second call on an already-issued application is a no-op, so a
capture and a webhook racing each other can never double-issue.

## Money truth: PayPal, and only PayPal

`PayPalService` is the only code that talks to PayPal (Orders API,
sandbox): OAuth token, create order, capture, and webhook-signature
verification. `CheckoutService` owns the rules around it:

- **Paid lands only from a COMPLETED capture** (directly, or via the
  verified webhook acting on one) — never from a buyer return page.
  The Return/Cancel endpoints are capture-or-release plumbing that
  redirect with a banner and render no success state.
- **Create-order is idempotent**: a Pending application that already
  holds an order id returns that id; a second click never mints a
  second order.
- A PENDING capture keeps the application Pending; other unsuccessful
  captures fail without issuance. An Employee or already-Paid create
  answers "not required" and creates no order.
- Inbound webhooks are idempotent through the `WebhookEventLog`
  table (Provider + EventId unique): a redelivered event is a
  logged no-op. A webhook whose amount disagrees with the
  application's price snapshot marks it Failed and issues nothing.
- Missing PayPal configuration is an honest 503, never a fake
  success. Secrets arrive only via environment variables or
  user-secrets; the tree carries placeholders only.

## The student's surfaces

- **My Permits** (`/Permits/My`) — the person's own applications,
  newest first, with summary tiles (total / Paid / pending payment)
  and Duvara's column set including Category and "Not Required"
  spelled out. "Pay Now" appears only on price > 0 + unpaid rows;
  "Permit (PDF)" only on live rows. No search, filter, or
  pagination — as in Duvara.
- **Receipt + permit PDF** — PDFsharp (MIT) and embedded Roboto (Apache-2.0; see
  `NOTICES.md`), regenerated on every call. Gates mirror Duvara's
  status codes: a non-owner gets 403, an unknown id 404, an owned
  but not-yet-live application 400 with a plain-language body. The
  document carries the category, the privilege line (Staff vs
  Student/Public Parking Eligible), an EXEMPT (Employee) payment
  line where it applies, the PayPal order/capture ids, and a
  "Proof of Parking (For Enforcement)" section with the public
  verify URL and token.
- **Public verification** — `/verify?t=` answers anonymously from
  the token alone (VALID / not valid, with type, plate state,
  category, privilege line, payment status word, and last-updated
  stamp; unknown tokens get a neutral page and no other permit's
  data). `GET /api/verify?t=` is its JSON twin — same verdict, same
  fields. Both sit behind a light in-memory rate limit
  (60 requests/minute/IP). Validity itself is one rule,
  `PermitValidity.Check`: start ≤ day ≤ end, status not Denied,
  and payment Paid or NotRequired.

## The college side

The roster (`/College/Roster`) shows, per campus, who paid and who
didn't, plus Duvara's three admin tiles — total permits issued,
permit revenue (the sum of Paid price snapshots), and the
expired-permit count — and a "By student" section. `PriceRepairService`
is the one repair tool: it recomputes **Pending** applications'
prices from the fee catalog and reports every change; Paid and
NotRequired snapshots are historical truth and are never rewritten.

## Data and concurrency

EF Core + SQLite (`EnsureCreated`; there is no migration framework
and no database server for a judge to install). `PermitApplication`
carries a Guid `ConcurrencyToken`; every service that saves an
application rotates it first, so a stale write fails loudly instead
of silently overwriting. The duplicate rule is a composite unique
index on (StudentId, VehicleId, PermitTypeId, StartDate, EndDate);
both doors catch its violation and answer with Duvara's
"already submitted…" message on the same screen.

## The hackathon layer around the core

These live outside the permit core and touch it only through its
contracts (details and honesty limits in `docs/SPONSORS.md`):

- **Fee table** — AG Grid Community over the fee catalog, with a
  native server-rendered fallback behind the same flag; both
  renderers show identical values.
- **College search** — built-in search is the default and labels
  itself on screen; an Elastic provider sits behind a config seam
  (`scripts/elastic/`) with fallback to the built-in provider.
- **Zapier** — an outbound payment-complete webhook fires after a
  Paid/free truth is saved. It never blocks a payment and carries
  no student name, email, or plate — the payload is the permit
  number, campus, amount, capture id, `receiptIssuedAtUtc`, and
  the public verify URL.
- **Postman** — `postman/` ships a create → capture → verify →
  extract collection that asserts the honest 503 shape when PayPal
  is not configured.
- **KERNEL fee-refresh** — `tools/fee-refresh/` re-checks the
  published campus fees offline against `data/fees.csv`.
- **AI data path** — the browser sends the sentence to Solvia; the app
  calls the selected provider. Ollama is local to the app host by default
  but its endpoint is configurable. Selecting Meta sends the sentence and
  extraction context to Meta's hosted API. Standard mode excludes content
  from model training; this is not a zero-retention or US-only-residency
  guarantee. See [PRIVACY.md](PRIVACY.md) for current terms and boundaries.
