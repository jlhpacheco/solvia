# Solvia — Data Schema (permits only)

Solvia keeps the data model small on purpose. One record does the whole job:
**a `PermitApplication` that reaches Paid *is* the permit.** There is no second
"permit" table to keep in sync, and the receipt number issued at payment time
doubles as the permit number.

## Entities

### `PermitApplication` — the permit itself

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `StudentId` | `string` | Owner of the application; set on the server, never trusted from the form |
| `VehicleId` | `Guid` | The vehicle this permit covers |
| `PermitTypeId` | `int` | Which term/type was bought |
| `StartDate`, `EndDate` | `DateOnly` | The permit's valid window |
| `Status` | enum | `Pending` → `Approved` / `Denied` |
| `PriceCents` | `int` | Exact price snapshot, in cents, taken from the college's real fee row at purchase time |
| `PaymentStatus` | enum | `NotRequired`, `Pending`, `Paid`, `Failed`, `Refunded`, `Expired` |
| `PayPalOrderId` | `string?` | PayPal order created at checkout |
| `PayPalCaptureId` | `string?` | PayPal capture returned when the money lands |
| `PaidAtUtc` | `DateTime?` | Set once, only when the payment is truly Paid |
| `ReceiptNumber` | `string?` | Issued at payment; **this is the permit number** (unique) |
| `VerifyToken` | `string?` | Unguessable token for public permit verification |
| `SubmittedAtUtc` | `DateTime` | When the student applied |

### `PermitType` — the catalog of what can be bought

| Field | Type | Notes |
|---|---|---|
| `Id` | `int` | Primary key |
| `Code` | enum, stored as text | `Daily`, `Term` |
| `Name` | `string` | Display name, e.g. "Fall term" |
| `DurationDays` | `int` | How long the permit lasts |
| `PriceCents` | `int` | Catalog price in cents |
| `Active` | `bool` | Only active types are offered to students |

### `Vehicle` — the student's car

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `StudentId` | `string` | Owner; set on the server |
| `Plate` | `string` | License plate — the lookup field on campus |
| `State` | enum | Plate state, default `CA` |
| `Color` | enum | Vehicle color |
| `Make`, `Model` | `string?` | Optional |

### College fee data — `data/fees.csv`

Not a database table: a plain CSV of real published parking-permit fees for 25
California campuses. The AI reads the student's college row from this file so
the total it quotes — and the amount sent to PayPal — is the college's real
fee, never a generic or invented price.

## The PayPal flow (step names)

1. **Create order** — the student picks college, car, and term. Solvia takes
   the exact total from that college's fee row and creates a PayPal order in
   the **sandbox**.
2. **Approve** — the student reviews and approves the payment on PayPal.
3. **Capture** — Solvia captures the approved order.
4. **Paid is the truth** — an application becomes Paid only from the capture
   result / webhook confirmation, never from a "success" return page. When
   Paid lands, Solvia stamps `PaidAtUtc`, stores the PayPal capture ID, and
   issues the `ReceiptNumber` — which is the permit number the student shows
   on campus.

## Guard rules

- **No duplicate permits:** a unique index on
  (`StudentId`, `VehicleId`, `PermitTypeId`, `StartDate`, `EndDate`) stops the
  same permit being sold twice.
- **A permit is active** when today falls inside `StartDate`–`EndDate`, the
  application is not Denied, and `PaymentStatus` is Paid (or NotRequired for
  a free permit).

## Out of scope — by design

Solvia is permits only. There are **no** citations, appeals, hearings, or
payment plans in this schema, and none will be added for the hackathon build.
