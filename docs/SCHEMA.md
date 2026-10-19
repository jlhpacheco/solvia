# Solvia — Data Schema (permits only)

Solvia keeps the data model small on purpose. One record does the whole job:
**a `PermitApplication` that reaches Paid *is* the permit.** There is no second
"permit" table to keep in sync, and the receipt number issued at the live
transition doubles as the permit number.

This document describes the **REDO** model, verified field-for-field against
`src/Solvia/Models/`, `src/Solvia/Models/Enums/`,
`src/Solvia/Data/SolviaDbContext.cs`, `src/Solvia/Services/CampusFee.cs`,
`src/Solvia/Services/IFeeCatalog.cs`, and `data/fees.csv`. Stack: .NET 10
ASP.NET Core MVC, EF Core + SQLite (a single local file, zero server install).
All enums below are stored in SQLite as **TEXT** via EF Core string
conversions (the only exception is the storage shape noted per field), never
as bare integers.

Five tables: `Student`, `PermitType`, `Vehicle`, `PermitApplication`,
`WebhookEventLog`. Plus one non-table price source: `data/fees.csv`, parsed
into the `CampusFee` record — the **only** source of prices in Solvia.

## Entities

### `Student` — a seeded, fictional demo person

Solvia has no login stack. The people in the database are seeded, fictional
demo people; `Id` is a short slug assigned on the server and is the owner key
stamped onto vehicles and permit applications (never trusted from a form).

| Field | Type | Notes |
|---|---|---|
| `Id` | `string` | Primary key. Server-set owner id, e.g. `"maria"`. Max length 64. |
| `DisplayName` | `string` | Display name, e.g. `"Maria"`. Required, max length 100. |
| `Email` | `string` | Fictional contact address — always an `.example` domain. Required, max length 200. The email domain drives category resolution (see `UserCategoryResolver`). |
| `CategoryOverride` | `UserCategory?` | Per-person category override. When set, it **wins over the email-domain rule** in `UserCategoryResolver` — the demo equivalent of an admin-set category flag. Stored as TEXT, max length 20, nullable. Seeded `null` for every fictional person; no screen edits it. |

Seeded cast (all `CategoryOverride = null`):

| `Id` | `DisplayName` | `Email` | Resolved category |
|---|---|---|---|
| `maria` | Maria | `maria@student.example` | Student |
| `diego` | Diego | `diego@student.example` | Student |
| `priya` | Priya | `priya@student.example` | Student |
| `sam` | Sam | `sam@student.example` | Student |
| `elena` | Elena | `elena@college.example` | Employee |
| `pat` | Pat | `pat@public.example` | Public |

Category resolution order (`UserCategoryResolver`): 1) `CategoryOverride`
if set; 2) email domain — a domain in `CategoryOptions.EmployeeDomains`
(default `["college.example"]`) → Employee, else a domain in
`CategoryOptions.StudentDomains` (default `["student.example"]`) →
Student (an address listed in both resolves Employee — the employee list is
consulted first); 3) otherwise Public. Faculty and Visitor exist as
categories (they price like Public) but no seeded person resolves to them
by domain, and no screen sets them.

### `PermitType` — the catalog of what can be bought

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key. |
| `Code` | `PermitTypeCode`, stored as TEXT | Catalog code: `Daily` or `Term`. Required, max length 20. Unique index on `Code`. |
| `Name` | `string` | Display name, e.g. `"Term / semester permit"`. Required, max length 200. |
| `DurationDays` | `int` | How long a permit of this type lasts, in days. |
| `PriceCents` | `int` | **Seeded `0` — a NON-AUTHORITATIVE placeholder, never a price source.** The amount anyone actually pays is resolved from the college's published fee row (`data/fees.csv`) by `PricingService` and snapshotted onto `PermitApplication.PriceCents`. No code path may charge from this column — a guard test pins that. |
| `Active` | `bool` | Only active types are offered on the Apply form. |

Seeded rows (exactly two):

| `Id` | `Code` | `Name` | `DurationDays` | `PriceCents` | `Active` |
|---|---|---|---|---|---|
| 1 | `Daily` | Daily permit | 1 | 0 (placeholder) | `true` |
| 2 | `Term` | Term / semester permit | 120 | 0 (placeholder) | `true` |

> **Price truth is the per-college catalog, not this table.** If you are
> looking for "what does a permit cost?", the answer lives in
> `data/fees.csv` / `CampusFee` — never in `PermitType.PriceCents`.

### `Vehicle` — a person's car

Owner-scoped: `StudentId` is set on the server, and every lookup must filter
by it — a person only ever sees their own vehicles.

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `StudentId` | `string` | Owner of this vehicle; set on the server. Foreign key → `Student.Id`, delete behavior Restrict. Indexed. |
| `Student` | `Student?` | Navigation property (owner). |
| `Plate` | `string` | License plate — the lookup field on campus. Required, max length 20. |
| `State` | `UsState`, stored as TEXT | Plate state; defaults to `CA`. Required (conversion), max length 2. |
| `Color` | `VehicleColor`, stored as TEXT | Vehicle color. Required (conversion), max length 20. |
| `Make` | `string?` | Optional. Max length 50. |
| `Model` | `string?` | Optional. Max length 50. |

### `PermitApplication` — the permit itself

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `StudentId` | `string` | Owner of the application; set on the server, never trusted from the form. Foreign key → `Student.Id`, delete behavior Restrict. Part of the composite unique index (see Guard rules). |
| `Student` | `Student?` | Navigation property (owner). |
| `Campus` | `string` | The campus this permit is for — the canonical name from the fee catalog, snapshotted at creation next to the price, so the receipt, My Permits, and the roster can show the college. Required, max length 200. |
| `Category` | `UserCategory`, stored as TEXT | The buyer's category, resolved by `UserCategoryResolver` and **snapshotted at creation** — pricing, the roster, and the PDF read this snapshot, never a re-resolution. Default `Public`. Required (conversion), max length 20. |
| `VehicleId` | `Guid` | The vehicle this permit covers. Foreign key → `Vehicle.Id`, delete behavior Restrict. Part of the composite unique index. |
| `Vehicle` | `Vehicle?` | Navigation property. |
| `PermitTypeId` | `int` | Which catalog type was bought (Daily / Term). Foreign key → `PermitType.Id`, delete behavior Restrict. Part of the composite unique index. |
| `PermitType` | `PermitType?` | Navigation property. |
| `Term` | `TermKind`, stored as TEXT | The term the buyer picked (Fall / Spring / Summer) — stored on the row so the price can be recomputed from the fee catalog without trusting anything the browser posts. Default `Fall`. Required (conversion), max length 20. |
| `Source` | `string` | Which door/reader produced the application — the honest source label, kept with the row so it survives redirects: `"form"` (form door), `"ollama"` (local AI answered), or `"fallback"` (deterministic fallback answered). Default `"form"`. Required, max length 16. |
| `StartDate` | `DateOnly` | First day the permit is valid. Part of the composite unique index. |
| `EndDate` | `DateOnly` | Last day the permit is valid (always computed, never typed). Part of the composite unique index. |
| `Status` | `ApplicationStatus`, stored as TEXT | Application standing. Default `Pending`. Required (conversion), max length 20. |
| `PriceCents` | `int` | Exact price snapshot, in cents, resolved from the college's real published fee row at creation time. This — never the `PermitType` row's placeholder — is the amount sent to PayPal. An Employee snapshot is `0` (exempt, applied *after* catalog resolution); a FREE campus also snapshots `0` as a real price. |
| `PaymentStatus` | `PaymentStatus`, stored as TEXT | Payment state. Default `Pending`. Required (conversion), max length 20. |
| `PayPalOrderId` | `string?` | PayPal order created at checkout (sandbox). Max length 64. |
| `PayPalCaptureId` | `string?` | PayPal capture returned when the money lands. Max length 64. |
| `PaidAtUtc` | `DateTime?` | Set once, only when the payment is truly Paid. |
| `ReceiptNumber` | `string?` | Issued at the live transition; **this IS the permit number** (unique). Max length 32. Unique index — SQLite unique indexes still allow many `NULL`s pre-issuance. |
| `ReceiptIssuedAtUtc` | `DateTime?` | When the receipt/permit number was minted. Null until issuance; stamped by the one issuance path (`IssuanceService.IssueIfAbsentAsync`) only, at the same moment it mints the number and the verify token. |
| `VerifyToken` | `string?` | Unguessable token for public permit verification (`/verify`, `/api/verify`). Max length 64. Unique index (many `NULL`s allowed pre-issuance). |
| `SubmittedAtUtc` | `DateTime` | When the person applied. |
| `ConcurrencyToken` | `Guid` | Optimistic-concurrency token, configured with EF Core `IsConcurrencyToken()`. Every service that saves an application sets a fresh `Guid` first; a writer holding a stale token gets a `DbUpdateConcurrencyException` instead of silently overwriting a sibling's save. (A `Guid` stands in for a rowversion column — SQLite has none.) |

There is no separate "permit" table: a `PermitApplication` whose payment
lands Paid **is** the permit, and `ReceiptNumber` doubles as the permit
number shown on campus. Issuance is mint-if-absent and happens through
exactly one path (`IssuanceService`), called from exactly three places: a
$0 application at creation, a COMPLETED PayPal capture, and a verified
PayPal webhook — never from a return page, a receipt page, or the PDF.

### `WebhookEventLog` — one row per inbound webhook delivery

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key. |
| `Provider` | `string` | Who sent the event, e.g. `"PayPal"`. Required, max length 32. Part of the unique idempotency index. |
| `EventId` | `string` | The provider's own event id (unique per provider). Required, max length 128. Part of the unique idempotency index. |
| `EventType` | `string` | The provider's event type, e.g. `"PAYMENT.CAPTURE.COMPLETED"`. Required, max length 128. |
| `ReceivedAtUtc` | `DateTime` | When the delivery reached us. |
| `ProcessedAtUtc` | `DateTime?` | When handling finished; null while unhandled. |
| `Outcome` | `string?` | How the delivery ended, in the handler's words (`"Acted"`, `"Duplicate"`, `"Ignored"`, `"Failed"`). Null until handled. Max length 32. |

**Idempotency:** the unique index on (`Provider`, `EventId`) is what makes
webhook processing idempotent — a redelivered event collides with its own
log row instead of acting twice.

## Enum value sets (complete)

All enums are stored as TEXT (see the per-entity conversion notes above).

### `ApplicationStatus`

`Pending`, `Approved`, `Denied`

Created `Pending`; no admin approve/deny action exists in the demo — permit
validity is computed around Pending-counts-when-paid (see Guard rules).

### `PaymentStatus`

`NotRequired`, `Pending`, `Paid`, `Failed`, `Refunded`, `Expired`,
`PartiallyRefunded`

An application is a live permit only when this is `Paid` — or `NotRequired`
for a free permit. `Paid` is set **only** from a PayPal capture result /
verified webhook confirmation, never from a checkout return page.

### `PermitTypeCode`

`Daily`, `Term`

### `TermKind`

`Fall`, `Spring`, `Summer`

Fall and Spring are the regular semesters; Summer is the shorter summer
session.

### `UserCategory`

`Student`, `Employee`, `Public`, `Faculty`, `Visitor`

Who a person is, for pricing. Employee is exempt ($0, applied after catalog
resolution); Faculty and Visitor price exactly like Public.

### `VehicleColor`

`Black`, `White`, `Silver`, `Gray`, `Red`, `Blue`, `Green`, `Brown`,
`Orange`, `Yellow`, `Gold`, `Purple`, `Other`

### `UsState`

`AL`, `AK`, `AZ`, `AR`, `CA`, `CO`, `CT`, `DE`, `DC`, `FL`, `GA`, `HI`,
`ID`, `IL`, `IN`, `IA`, `KS`, `KY`, `LA`, `ME`, `MD`, `MA`, `MI`, `MN`,
`MS`, `MO`, `MT`, `NE`, `NV`, `NH`, `NJ`, `NM`, `NY`, `NC`, `ND`, `OH`,
`OK`, `OR`, `PA`, `RI`, `SC`, `SD`, `TN`, `TX`, `UT`, `VT`, `VA`, `WA`,
`WV`, `WI`, `WY`

All 50 states plus `DC` (51 values). Default `CA`.

### Service-layer enums (not stored columns)

- `ExtractionSource` (`IAiExtractor.cs`): `Ollama`, `Fallback` — which path
  produced an extraction. Serialized on the wire as `"ollama"` /
  `"fallback"`; these are the AI-door values stamped onto
  `PermitApplication.Source` (the form door stamps `"form"`).
- `RosterPermitState` (`PermitValidity.cs`): `Paid`, `FreeIssued`,
  `Pending`, `NoPermit` — a student's computed standing at one campus on
  the college roster. Computed by `PermitValidity.Classify`; never stored.

## College fee data — `data/fees.csv` → `CampusFee`

Not a database table: a plain CSV of real published parking-permit fees for
**exactly 10 California colleges** — every one with a real published permit
price. Semester prices run from **FREE to $229.50**, median **$50**. The AI
reads the student's college row from this file so the total it quotes — and
the amount sent to PayPal — is the college's real fee, never a generic or
invented price. If a campus does not publish a price for something, that
price is `null` ("not listed") — never `0` and never invented; an
unpublished price **blocks** the application. A campus that publishes FREE
permits has price `0` with `IsFree = true` — a real $0, distinct from null.

### CSV columns (header, verbatim)

`Campus`, `Student semester fee`, `Other term fees`, `Day pass`,
`Motorcycle`, `Citation (no-permit / range)`, `Terms/dates listed`,
`Source note`

The 10 rows (CSV order): CSU Monterey Bay ($229.50/semester); CSU East Bay
($195 Fall / $195 Spring); CSU Bakersfield ($93 Fall); Modesto Junior
College / Columbia College / YCCD (FREE); Contra Costa College ($50
Fall/Spring); Diablo Valley College ($50 Fall/Spring); Los Medanos College
($50 Fall/Spring); Antelope Valley College ($28 Fall/Spring); State Center
Community College District ($30 Fall/Spring); Fresno City College ($30
Fall/Spring).

### `CampusFee` record — the parsed shape

`CampusFee` is a sealed record in `Solvia.Services`, loaded eagerly by
`FeeCatalogService` (a missing file fails fast) and served read-only
through `IFeeCatalog` (`Find` — case-insensitive, whitespace-trimmed;
`All` — CSV order; `SemesterPriceCents(fee, term)`).

| Member | Type | Notes |
|---|---|---|
| `Campus` | `string` | Canonical campus name, from the CSV `Campus` column. Snapshotted onto `PermitApplication.Campus` at creation. |
| `StudentSemesterFeeText` | `string` | Raw text of the CSV `Student semester fee` column (e.g. `"$229.50 per semester (…)"`, `"FREE (…)"`, `"not listed on PMB page"`). Kept verbatim for display/audit. |
| `OtherTermFeesText` | `string` | Raw text of the CSV `Other term fees` column. The Summer price is parsed out of this cell (see parsing rules). |
| `DayPassText` | `string` | Raw text of the CSV `Day pass` column. `DayPassCents` is parsed from this text. |
| `MotorcycleText` | `string` | Raw text of the CSV `Motorcycle` column. Display/research text only — no motorcycle price is resolved from it. |
| `FallSemesterCents` | `int?` | Parsed Fall semester price in cents; `null` when not published. |
| `SpringSemesterCents` | `int?` | Parsed Spring semester price in cents; `null` when not published. |
| `SummerSemesterCents` | `int?` | Parsed Summer price in cents (the `Summer`-labelled amount in `OtherTermFeesText`); `null` when not published. |
| `IsFree` | `bool` | `true` when the campus publishes FREE permits (semester cell starts with `FREE`) — then Fall/Spring parse to `0`, a real price. |
| `SourceNote` | `string` | Raw text of the CSV `Source note` column — provenance of the researched fee (which public page was opened). |
| `DayPassCents` | `int?` | Init-only. The campus's published per-day price in cents, parsed from `DayPassText` by the catalog loader (label-aware rule below). `null` when the campus publishes no readable day-pass price — a Daily permit for such a campus is blocked, never priced by invention. |
| `HasPublishedSemesterPrice` | `bool` (computed) | `true` when the campus publishes at least one semester price: `FallSemesterCents.HasValue \|\| SpringSemesterCents.HasValue` (FREE counts). |

Two CSV columns are carried in the file as research context but are **not**
mapped into `CampusFee`: `Citation (no-permit / range)` and
`Terms/dates listed`. Term dates come from the term-dates service, not from
this CSV.

### Parsing rules (`FeeCatalogService`)

- **Semester fees:** a cell starting with "not listed" ⇒ Fall/Spring `null`.
  A cell starting with `FREE` ⇒ Fall/Spring `0` and `IsFree = true`.
  Otherwise, per `;`-segment, the first dollar amount labelled Fall wins
  for Fall, the first labelled Spring wins for Spring, and an unlabelled
  "per semester" amount applies to **both** semesters.
- **Summer fee:** the first `Summer`-labelled segment of the
  `Other term fees` cell supplies `SummerSemesterCents`; "not listed" or
  no Summer segment ⇒ `null`.
- **Term lookup** (`IFeeCatalog.SemesterPriceCents`): Fall reads
  `FallSemesterCents ?? SpringSemesterCents`; Spring reads
  `SpringSemesterCents ?? FallSemesterCents`; Summer reads
  `SummerSemesterCents` only (no fallback).
- **Day pass — the label-aware rule:** read the **first** `;`-segment only
  (later segments are staff/other-audience rates). Inside that segment, an
  amount explicitly labelled as the day rate — the text right after the
  amount starts with `/day`, `per day`, or `a day` — **beats** an earlier
  unlabelled or hourly amount; otherwise the first amount wins. "Not
  listed" / no amount ⇒ `null`. This is what parses the trap cell
  `"$2/hour or $10/day (dispensers)"` to **$10.00 (1000 cents)**, not $2 —
  and `"$5 Student/Other; $2 Staff …"` to the student-facing **$5.00**.

### How a price is resolved (`PricingService` — the one price function)

- Term → `IFeeCatalog.SemesterPriceCents(fee, term)`; Daily →
  `CampusFee.DayPassCents`. `null` means **blocked** — never a fallback
  number, never the `PermitType` placeholder.
- The Employee exemption applies **after** resolution: the catalog is
  consulted first, so an unpublished fee blocks even an Employee
  application instead of silently issuing it; a resolved fee for an
  Employee buyer snapshots to `0`. Faculty and Visitor price like Public.
- An unknown campus name resolves to `null` (blocked), exactly like an
  unpublished fee.

## The PayPal flow (step names)

1. **Create order** — the student picks college, car, and term. Solvia takes
   the exact total from that college's fee row (snapshotted as
   `PermitApplication.PriceCents`) and creates a PayPal order in the
   **sandbox**.
2. **Approve** — the student reviews and approves the payment on PayPal.
3. **Capture** — Solvia captures the approved order.
4. **Paid is the truth** — an application becomes Paid only from the capture
   result / verified webhook confirmation, never from a "success" return
   page. When Paid lands, Solvia stamps `PaidAtUtc`, stores the PayPal
   capture ID (`PayPalCaptureId`), and — through the one issuance path —
   mints the `ReceiptNumber` (the permit number), the `VerifyToken`, and
   the `ReceiptIssuedAtUtc` stamp. A $0 application (FREE campus or
   Employee-exempt) skips PayPal entirely: it is issued at creation with
   `PaymentStatus = NotRequired`.

Webhooks: every inbound PayPal delivery is logged to `WebhookEventLog`
first; the unique (`Provider`, `EventId`) index makes redelivery a
no-op duplicate instead of a second action.

## Guard rules

- **No duplicate permits:** a unique composite index on
  (`StudentId`, `VehicleId`, `PermitTypeId`, `StartDate`, `EndDate`) stops
  the same person buying the same type, for the same vehicle, over the
  same window twice.
- **Unique once issued:** unique indexes on `ReceiptNumber` and on
  `VerifyToken` (SQLite still allows many `NULL`s before issuance).
- **Unique catalog code:** a unique index on `PermitType.Code`.
- **Referential integrity:** `Vehicle.StudentId`,
  `PermitApplication.StudentId`, `PermitApplication.VehicleId`, and
  `PermitApplication.PermitTypeId` are foreign keys with delete behavior
  Restrict. `PermitApplication` lookups by owner always filter on
  `StudentId`; `Vehicle` has an index on `StudentId`.
- **Optimistic concurrency:** every writer rotates
  `PermitApplication.ConcurrencyToken` before saving; EF Core checks the
  loaded value on UPDATE.
- **A permit is active** (`PermitValidity` — the one place this is
  decided) when today falls inside `StartDate`–`EndDate`, the application
  is not Denied, and `PaymentStatus` is Paid (or NotRequired for a free
  permit). A Refunded permit is never active. The college roster and the
  public `/verify` page + `/api/verify` JSON twin both read their truth
  from here.
- **Startup validation** (`SolviaDbContext.ValidateStartupData`): the app
  refuses to start on a broken catalog — it throws an
  `InvalidOperationException` naming the failed check unless exactly the
  Daily and Term types are seeded, both Active, both with
  `DurationDays > 0`, the fee catalog holds at least one campus with a
  published semester (Term) price, and at least one campus with a
  parseable day-pass price.

## Configuration surface (`src/Solvia/Config/AppOptions.cs`)

Not tables — the typed options the schema's behavior depends on. Secrets
(`PayPal.ClientId` / `PayPal.ClientSecret`) are supplied **only** through
environment variables or .NET user-secrets, never through committed files.

| Section / class | Members (defaults) |
|---|---|
| `PayPal` / `PayPalOptions` | `ClientId` (`""`), `ClientSecret` (`""`), `Environment` (`"sandbox"`), `BaseUrl` (`"https://api-m.sandbox.paypal.com"`), `Currency` (`"USD"`), `WebhookId` (`""` — id of the registered PayPal webhook, used by verify-webhook-signature; an identifier, not a credential) |
| `Ollama` / `OllamaOptions` | `BaseUrl` (`"http://localhost:11434"`), `Model` (`"phi4-mini"`) |
| `Category` / `CategoryOptions` | `EmployeeDomains` (`["college.example"]`), `StudentDomains` (`["student.example"]`) |
| `FeeTable` / `FeeTableOptions` | `Renderer` (`"native"`; `"aggrid"` is the AG Grid Community seam) |
| `Calendar` / `CalendarOptions` | `Renderer` (`"native"`; `"bryntum"` is the licensed-build drop-in point) |
| `Search` / `SearchOptions` | `Provider` (`"builtin"`; `"elastic"` routes through a local Elasticsearch index with built-in fallback) |
| `Elastic` / `ElasticOptions` | `Url` (`"http://localhost:9200"`), `Index` (`"colleges"`) |
| `Webhook` / `WebhookOptions` | `Url` (`""` = disabled — the outbound payment-complete / Zapier seam; events carry no student PII), `PublicBaseUrl` (`""` = no fallback origin; an origin is never invented) |
| Root / `SolviaOptions` | `FeesCsvPath` (`"data/fees.csv"`) |

## Out of scope — by design

Solvia is permits only. There are **no** citations, appeals, hearings, or
payment plans in this schema, and none will be added for the hackathon build.
