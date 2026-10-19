# Solvia API context — authored for the APIMatic Context Plugin

**What this file is.** This document is the API context for **Solvia's
own API surface**, written in the markdown context form that
**APIMatic's Context Plugin** consumes: a coding agent (or a developer)
reads it to generate and call Solvia's endpoints correctly — endpoint
purposes, exact request/response shapes, auth model, and every error
state, including the failure shapes that are part of the design.

**Honest status.** This context document was **authored for the
plugin** as Solvia's APIMatic artifact. Running the Context Plugin
itself happens in Jose Luis's editor, where the plugin is installed —
that run has **not** happened yet, and nothing in this repo claims it
has. Everything below is verified against the shipped code
(`src/Solvia/Controllers/`), not aspirational.

---

## API overview

- **Product:** Solvia — a fictional student picks her college, a local
  AI (Microsoft Phi-4-mini via Ollama, 100% on the student's own
  computer) asks 3 questions (school / car / term), the app quotes the
  exact total from the college's real published fee (`data/fees.csv`,
  10 campuses), the student approves, and pays with **PayPal sandbox**.
  A paid application IS the permit; its receipt number is the permit
  number.
- **Base URL (local demo):** `http://localhost:5156`
  (`dotnet run --project src/Solvia`; the port changes if the app is
  started with `--urls`).
- **Style:** the product is a server-rendered ASP.NET Core MVC app.
  The machine-readable surface is three JSON endpoints plus one
  public HTML verification endpoint, listed below. Everything else
  is browser pages (Maria's flow: `/Permits/Apply`,
  `/Permits/Questions`, `/Permits/Review`, `/Checkout/{id}`,
  `/Checkout/Done/{id}`, `/Permits/Receipt/{id}`; the college side:
  `/College/Fees`, `/College/Roster`, `/College/Dates`).
- **Upstream dependency:** PayPal **sandbox only**
  (`https://api-m.sandbox.paypal.com`) — fictitious accounts, no real
  money. The app calls PayPal server-side; clients of this API never
  talk to PayPal directly and never see PayPal credentials.

## Authentication

- **None — demo API.** No API keys, no OAuth, no bearer tokens on any
  endpoint below.
- The demo runs on **seeded fictional students** (Maria and classmates).
  The student's identity (`StudentId`) is set **on the server** from
  the demo session; clients never supply a student identity to these
  endpoints, and no endpoint accepts one.
- `GET /verify` is **anonymous by design**: the credential is the
  unguessable `VerifyToken` issued at payment time, passed as the `t`
  query parameter. It names exactly one permit.
- PayPal credentials (sandbox client id/secret) live only in the
  server's environment (env vars / .NET user-secrets). They are never
  accepted from, returned to, or logged for API clients.

## Endpoints

### POST /api/paypal/create-order

Creates the PayPal sandbox order for one permit application. Called
by the checkout page's PayPal JS SDK buttons; the SDK expects the new
order id back as `{ "id": ... }`.

The amount is **never** in the request. The server charges the
application's snapshotted `PriceCents` — the exact fee from the
college's published `fees.csv` row, fixed at purchase time.

**Request** (`application/json`):

```json
{ "permitApplicationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

| Field | Type | Notes |
|---|---|---|
| `permitApplicationId` | string (UUID) | The permit application being paid for |

**Responses:**

| Status | Body | Meaning |
|---|---|---|
| 200 | `{ "id": "8AB12C3DE456789F" }` | Sandbox order created; send the buyer to approve it on sandbox.paypal.com |
| 200 | `{ "id": null, "free": true }` | The permit is $0 (FREE college fee): confirmed in place, **no PayPal call was made** |
| 200 | `{ "id": "<existing order id>", "alreadyPaid": true }` | The permit was already Paid; nothing new was created |
| 404 | `{ "error": "Permit application '...' was not found." }` | Unknown application id |
| 503 | `{ "error": "...PAYPAL_CLIENT_ID..." }` | **Sandbox credentials are not configured on this machine** — see Error model. The message names the missing settings, never any value |
| 502 | `{ "error": "PayPal sandbox could not create the order. The permit is not paid." }` | PayPal's API errored; no order exists, nothing was charged |

### POST /api/paypal/capture

Captures an approved sandbox order and reports the standing truth.
This is the only road to Paid: the server marks the permit Paid
**only** when PayPal's capture response says `COMPLETED` — never
because a browser landed on a success page.

**Request** (`application/json`):

```json
{ "orderId": "8AB12C3DE456789F" }
```

| Field | Type | Notes |
|---|---|---|
| `orderId` | string | The PayPal order id returned by create-order |

**Responses:**

| Status | Body | Meaning |
|---|---|---|
| 200 | `{ "status": "COMPLETED", "completed": true, "captureId": "9XY…", "receiptNumber": "SOL-2026-483920" }` | Capture completed; the permit is Paid. `receiptNumber` (format `SOL-{year}-{6 digits}`) IS the permit number |
| 200 | `{ "status": "PENDING", "completed": false, "captureId": null, "receiptNumber": null }` | PayPal did not complete the capture; the application is marked Failed, **never** Paid |
| 404 | `{ "error": "No permit application found for PayPal order '...'." }` | Unknown order id |
| 503 | `{ "error": "...PAYPAL_CLIENT_ID..." }` | Sandbox credentials not configured (see Error model) |
| 502 | `{ "error": "PayPal sandbox could not capture the order. The permit is not paid." }` | PayPal's API errored; the permit is not paid |

Calling capture twice for the same order is safe: once Paid, the
endpoint reports the standing truth (`COMPLETED`, same capture id and
receipt number) without charging again.

### POST /api/chat/extract

Turns one student message, in her own words, into a validated permit
draft. Called by the student flow per message.

**Request** (`application/json`):

```json
{ "text": "I study at Fresno City College, I drive a blue Honda Civic, and I need a fall semester permit." }
```

| Field | Type | Notes |
|---|---|---|
| `text` | string, required | The student's message. Empty/missing text is a 400 |

**Response 200** (`application/json`) — every field may be `null`
except `source`; missing information stays null and the flow asks
only for what is missing (3 questions max):

```json
{
  "college": "Fresno City College",
  "permitType": "Term",
  "term": "Fall",
  "carMake": "Honda",
  "carModel": "Civic",
  "carColor": "Blue",
  "plate": null,
  "carState": null,
  "source": "ollama"
}
```

| Field | Type | Notes |
|---|---|---|
| `college` | string \| null | The fee catalog's **canonical** campus name — the extractor can only return a campus the catalog knows |
| `permitType` | `"Daily"` \| `"Term"` \| null | |
| `term` | `"Fall"` \| `"Spring"` \| `"Summer"` \| null | |
| `carMake`, `carModel`, `carColor`, `plate`, `carState` | string \| null | Plate normalized to upper case without spaces; state is a 2-letter code |
| `source` | `"ollama"` \| `"fallback"` | **Honest label of which path answered.** `"ollama"`: the local Phi-4-mini model answered and passed validation. `"fallback"`: the deterministic extractor answered because the model was down, timed out, or its answer failed validation — extraction always answers |

**There is deliberately NO price field** in this response, and the
model can never add one: the AI never sets the price. Prices come
only from the fee catalog, server-side.

| Status | Body | Meaning |
|---|---|---|
| 400 | `{ "error": "A student message is required." }` | `text` was empty or missing |
| 200 (always otherwise) | draft above | Model failures are not API errors — they land in the fallback and `source` says so |

### GET /verify?t={token}

Public permit verification — the page a campus officer opens from
the student's receipt link. **HTML, always HTTP 200.**

| Case | Page |
|---|---|
| Token names a live permit | **VALID** — permit number, campus, student name, valid dates |
| Token is real but the permit is not active (unpaid, denied, outside its dates) | **NOT VALID** — the permit number and campus, plus a plain-words reason |
| Token unknown or missing | The same neutral **NOT VALID** page ("We could not find a permit with that code") — never an error stack, never a hint about other permits |

## Error model

All JSON errors share one shape: `{ "error": "<plain-words message>" }`.

| Status | Where | Meaning |
|---|---|---|
| 400 | extract | The request itself is unusable (empty message) |
| 404 | create-order, capture | The named application/order does not exist |
| 502 | create-order, capture | PayPal's sandbox API errored. The permit is **not** paid; the message says exactly that |
| 503 | create-order, capture | **Not configured:** the sandbox client id/secret are not set on this machine. The message names the missing settings (`PAYPAL_CLIENT_ID` / `PAYPAL_CLIENT_SECRET`) and never contains a credential value. This is a designed, honest state — a fresh clone runs this way until the developer creates a free sandbox app and sets the keys (see the repo README) — not a crash |

## Money rules (context any generated client must respect)

1. **Paid is decided in exactly one place** — the server's capture
   handling, from a capture response whose status is `COMPLETED`.
   Return/success pages never mark anything paid.
2. **$0 is real and distinct:** a college whose published fee is $0
   (e.g. YCCD) gets a live permit with payment status `NotRequired`
   and **zero PayPal calls** — never a fake $0 "payment".
3. **No listed price is never $0:** a campus whose fee is not
   published cannot be charged an invented amount; the catalog returns
   "not listed", never 0.
4. **Amounts are integer cents server-side** (`PriceCents`), formatted
   to exact USD dollars only at the PayPal boundary — $229.50 is sent
   as `"229.50"`, never rounded.
