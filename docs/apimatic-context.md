# Solvia API context — authored for the APIMatic Context Plugin

**What this file is.** This document describes **Solvia's own API
surface as it exists in the redo controllers**, written in the
markdown context form that **APIMatic's Context Plugin** consumes: a
coding agent (or a developer) reads it to generate and call Solvia's
endpoints correctly — endpoint purposes, exact request/response
shapes, auth model, and every error state, including the failure
shapes that are part of the design. The flow it covers, in order:
**create order → capture → verify → extract + webhook** (the inbound
PayPal webhook and the outbound payment-complete webhook).

**Honest status — read this first.** This context document was
**authored for the APIMatic plugin**; **a plugin run is NOT claimed.**
Running the Context Plugin itself happens in Jose Luis's editor,
where the plugin is installed — that run has **not** happened, and
nothing in this repo claims it has. Everything below is verified
against the shipped code (`src/Solvia/Controllers/` and the services
they call), not aspirational.

---

## API overview

- **Product:** Solvia — a fictional student picks her college, the
  configured AI endpoint (Microsoft Phi-4-mini via Ollama) drafts her
  vehicle details for review, and the app quotes the exact total from
  the college's real published fee (`data/fees.csv`, 10 campuses),
  the student approves, and pays with **PayPal sandbox**. A paid (or
  free-issued) application IS the permit; its receipt number is the
  permit number.
- **Base URL (local demo):** `http://localhost:5156`
  (`dotnet run --project src/Solvia`; the port changes if the app is
  started with `--urls`).
- **Style:** the product is a server-rendered ASP.NET Core MVC app
  (.NET 10). The machine-readable surface is the JSON endpoints
  below, plus one public HTML verification endpoint. Everything else
  is browser pages (the student flow: `/Permits/Apply`,
  `/Permits/Questions`, `/Permits/Review`, `/Checkout/{id}`,
  `/Permits/Receipt/{id}`; the college side: `/College/Fees`,
  `/College/Roster`, `/College/Dates`, `/College/Search`).
- **Upstream dependency:** PayPal **sandbox only**
  (`https://api-m.sandbox.paypal.com`) — fictitious accounts, no real
  money. The app calls PayPal server-side; clients of this API never
  talk to PayPal directly and never see PayPal credentials.

## Authentication and caller identity

- **No API keys, no OAuth, no bearer tokens** on the endpoints below.
- The demo has **no login stack**. The "signed-in" person is one of
  the seeded fictional students, picked with the demo switcher and
  remembered in the `solvia_student` cookie (Maria is the default);
  the id is resolved **on the server** against the Students table
  (`DemoControllerBase`). Clients never supply a student identity to
  these endpoints, and no endpoint accepts one.
- `POST /api/paypal/orders` and `POST /api/paypal/capture` are
  **antiforgery-protected** (`[ValidateAntiForgeryToken]` in
  `PayPalController`): the checkout page's script sends the request
  verification token header. They are also **owner-checked**: an
  application/order belonging to a different demo person answers
  **403**.
- `POST /api/chat/extract` carries no antiforgery attribute in the
  redo controller; it accepts the JSON body described below.
- `GET /verify` and `GET /api/verify` are **anonymous by design**:
  the credential is the unguessable `VerifyToken` issued at the live
  transition, passed as the `t` query parameter. It names exactly one
  permit.
- `POST /api/paypal/webhook` is **antiforgery-exempt by design**:
  there is no browser and no demo person. Its credential is PayPal's
  signature headers, verified server-side before anything is
  trusted or saved (see that endpoint below).
- PayPal credentials (sandbox client id/secret) live only in the
  server's environment (env vars / .NET user-secrets). They are never
  accepted from, returned to, or logged for API clients.

## Endpoints

### POST /api/paypal/orders — create order

Creates the PayPal sandbox order for one permit application. Called
by the checkout page's PayPal JS SDK buttons; the SDK expects the new
order id back as `{ "id": ... }`. Create is **idempotent**: a pending
application that already holds an order id returns that stored id —
a second order is never created.

The amount is **never** in the request. The server charges the
application's snapshotted `PriceCents` — the exact fee from the
college's published `fees.csv` row, fixed at purchase time.

**Request** (`application/json`):

```json
{ "permitApplicationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6" }
```

| Field | Type | Notes |
|---|---|---|
| `permitApplicationId` | string (UUID) | The permit application being paid for. Property names are the controller's record fields, serialized camelCase |

**Responses:**

| Status | Body | Meaning |
|---|---|---|
| 200 | `{ "id": "8AB12C3DE456789F", "status": "created" }` | Sandbox order created (or the stored order re-returned); send the buyer to approve it on sandbox.paypal.com |
| 200 | `{ "id": null, "status": "not-required" }` | No payment is required — Employee exemption, or a genuinely $0 published fee — confirmed and issued in place, **no PayPal call was made** |
| 200 | `{ "id": "<existing order id>", "status": "already-paid" }` | The permit was already Paid; nothing new was created |
| 400 | `{ "error": "<plain-words message>" }` | The application's stored price cannot be honestly charged (it disagrees with, or is not published in, the fee catalog — `CheckoutPricingException`). PayPal was not called; the permit is not paid |
| 403 | `{ "error": "That permit belongs to a different demo person." }` | The application belongs to another demo person |
| 404 | `{ "error": "That permit application was not found." }` | Unknown application id |
| 503 | `{ "error": "...PAYPAL_CLIENT_ID (PayPal:ClientId)..." }` | **Sandbox credentials are not configured on this machine** — see Error model. The message names the missing settings, never any value |
| 502 | `{ "error": "PayPal sandbox could not create the order. The permit is not paid." }` | PayPal's API errored; no order exists, nothing was charged |

### POST /api/paypal/capture — capture

Captures an approved sandbox order and reports the standing truth.
This is the only browser-facing road to Paid: the server marks the
permit Paid **only** when PayPal's capture response says
`COMPLETED` — never because a browser landed on a success page.

**Request** (`application/json`):

```json
{ "orderId": "8AB12C3DE456789F" }
```

| Field | Type | Notes |
|---|---|---|
| `orderId` | string, required | The PayPal order id returned by the create-order endpoint above. Empty/missing is a 400 |

**Responses:**

| Status | Body | Meaning |
|---|---|---|
| 200 | `{ "status": "COMPLETED", "completed": true, "captureId": "9XY…", "receiptNumber": "SOL-2026-483920" }` | Capture completed; the permit is Paid and issued. `receiptNumber` IS the permit number |
| 200 | `{ "status": "<PayPal status>", "completed": false, "captureId": null, "receiptNumber": null }` | PayPal did not complete the capture; the application is marked Failed, **never** Paid |
| 400 | `{ "error": "An order id is required." }` | `orderId` was empty or missing |
| 400 | `{ "error": "<plain-words message>" }` | The application has no order to capture, or another invalid state (`InvalidOperationException`) |
| 403 | `{ "error": "That permit belongs to a different demo person." }` | The order's application belongs to another demo person |
| 404 | `{ "error": "No permit application owns that PayPal order." }` | Unknown order id |
| 503 | `{ "error": "...PAYPAL_CLIENT_ID (PayPal:ClientId)..." }` | Sandbox credentials not configured (see Error model) |
| 502 | `{ "error": "PayPal sandbox could not capture the order. The permit is not paid." }` | PayPal's API errored; the permit is not paid |

Calling capture twice for the same order is safe: once Paid, the
endpoint reports the standing truth (`COMPLETED`, same capture id
and receipt number) without calling PayPal again or charging again.

### GET /verify?t={token} — verify (HTML)

Public permit verification — the page a campus officer opens from
the student's receipt link or from the outbound webhook's
`verifyUrl`. **HTML, HTTP 200 for every verdict** (a tripped rate
limit is the one exception: 429, plain text — see Error model).

The page and its JSON twin (next endpoint) build **one** shared
result object (`VerifyViewModel` via `VerifyResultBuilder`), so the
two doors can never drift. Its fields are permit facts only: **no
student name, no email, and never the plate itself** — the plate
**state** is the most an anonymous check may see.

| Case | Page |
|---|---|
| Token names a live, in-dates permit | **VALID** — permit number, campus, permit type, plate state, category, privilege line, payment status, valid dates, last-updated stamp |
| Token is real but the permit is not active (unpaid, failed, denied, outside its dates, never issued) | **NOT VALID** — the same field set (permit number shown as "(not issued)" when none was minted), plus a plain-words reason |
| Token unknown or missing | The same neutral **NOT VALID** page ("We could not find a permit with that code. Check the code and try again.") — never an error stack, never a hint about other permits |

### GET /api/verify?t={token} — verify (JSON twin)

The JSON twin of the public permit check (`VerifyApiController`).
Same token, same `PermitValidity` verdict, same field set as the
`/verify` page — by construction, not by parallel code. **Always
HTTP 200**, including for an unknown or missing token (the neutral
not-found body below). The shared per-IP rate limiter (60/minute)
guards this door exactly as it guards the page; a tripped limit
answers **429** (see Error model).

**Response 200** (`application/json`, camelCase) — a live permit:

```json
{
  "found": true,
  "isValid": true,
  "reason": "<plain-words verdict reason>",
  "permitNumber": "SOL-2026-483920",
  "campus": "Fresno City College",
  "permitType": "Term parking permit",
  "plateState": "CA",
  "category": "Student",
  "privilege": "Student/Public Parking Eligible",
  "paymentStatus": "Paid",
  "startDate": "2026-08-17",
  "endDate": "2026-12-14",
  "lastUpdatedUtc": "2026-10-18T22:41:07.123Z"
}
```

| Field | Type | Notes |
|---|---|---|
| `found` | boolean | True when the token named a real application |
| `isValid` | boolean | The `PermitValidity` verdict for today |
| `reason` | string | Plain-words reason, on valid and not-valid answers alike |
| `permitNumber` | string \| null | The receipt (= permit) number; null on a found permit means it was never issued |
| `campus`, `permitType` | string \| null | Canonical campus name; permit type display name |
| `plateState` | string \| null | Plate **state only** — never the plate |
| `category` | string \| null | The application's snapshotted category word (Student / Employee / Public / …) |
| `privilege` | string \| null | "Staff Parking Eligible" for Employee permits, "Student/Public Parking Eligible" otherwise |
| `paymentStatus` | string \| null | Payment status in words, with `NotRequired` spelled out as "Not Required" |
| `startDate`, `endDate` | string (date) \| null | The permit's valid window |
| `lastUpdatedUtc` | string (date-time) \| null | Freshest stamp on the row: issued, else paid, else submitted |

**Not-found body** (unknown or missing token, still 200):
`found: false`, `isValid: false`,
`reason: "We could not find a permit with that code. Check the code and try again."`,
and every permit field null.

### POST /api/chat/extract — extract

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
| `college` | string \| null | The fee catalog's **canonical** campus name — the extractor can only return a campus the catalog knows; an unresolvable college voids a model answer outright |
| `permitType` | `"Daily"` \| `"Term"` \| null | Serialized from the `PermitTypeCode` enum name |
| `term` | `"Fall"` \| `"Spring"` \| `"Summer"` \| null | Serialized from the `TermKind` enum name |
| `carMake`, `carModel`, `carColor`, `plate`, `carState` | string \| null | Plate normalized to upper case with separators stripped; state is a 2-letter code |
| `source` | `"ollama"` \| `"fallback"` | **Honest label of which path answered.** `"ollama"`: the local Phi-4-mini model answered and passed validation. `"fallback"`: the deterministic extractor answered because the model was down, timed out, or its answer failed validation — extraction always answers |

**There is deliberately NO price field** in this response, and none
can appear: the extraction result type
(`ExtractionResult` in `src/Solvia/Services/IAiExtractor.cs`) has **no
price member by construction** — not hidden, not nullable, absent.
The AI never sets the price. Prices come only from the fee catalog,
server-side.

| Status | Body | Meaning |
|---|---|---|
| 400 | `{ "error": "A student message is required." }` | `text` was empty or missing |
| 200 (always otherwise) | draft above | Model failures are not API errors — they land in the fallback and `source` says so |

### POST /api/paypal/webhook — inbound PayPal webhook

PayPal's servers call this; there is no browser, no demo person, and
no antiforgery token. The controller
(`PayPalWebhookController`) reads the raw body untouched —
verification needs the exact bytes' text — and hands body + headers
to `PayPalWebhookHandler`. The endpoint answers with a **status code
and no documented JSON body**; the handler's outcome is logged
server-side.

**Request:** the raw PayPal webhook event JSON, with PayPal's
signature headers (`paypal-transmission-id`,
`paypal-transmission-time`, `paypal-transmission-sig`,
`paypal-cert-url`, `paypal-auth-algo`). The event's `id` keys
idempotency; the only event type that acts is
`PAYMENT.CAPTURE.COMPLETED`.

**Responses:**

| Status | Meaning |
|---|---|
| 200 | Handled — including duplicates (a logged no-op: never acts twice, never re-issues, never re-fires the outbound webhook), ignored event types, and unmatched captures — so PayPal stops retrying what has already had its say |
| 400 | The body is not JSON, or the delivery carries no event `id` to key idempotency on. Nothing is saved |
| 401 | Unverifiable: empty body, missing signature headers, PayPal credentials not configured (so the signature cannot be verified), or signature verification failed. Nothing is saved |

Money rule on this door: a verified `PAYMENT.CAPTURE.COMPLETED` acts
only when the event amount equals the application's snapshotted
`PriceCents` and the application is a payable one. A mismatch — or
an Employee / $0 application — marks a still-pending application
Failed with **no issuance**. An already-live permit (Paid, or
NotRequired for a free/Employee permit) is never dragged backwards.

### Outbound webhook — the payment-complete event (not an endpoint you call)

Not a Solvia endpoint: an event Solvia **sends**. When a permit goes
live (Paid, or free-issued) and that truth has been **saved**,
`PaymentWebhookService` POSTs one small JSON event to the configured
`Webhook:Url` (built for a Zapier Catch Hook — full recipe in
`docs/zapier-recipe.md`). It fires only on Paid / free-issue, never
blocks a payment, and carries **no student PII**.

```json
{
  "permitNumber": "SOL-2026-483920",
  "campus": "Fresno City College",
  "amountCents": 3000,
  "paypalCaptureId": "9XY…",
  "paidAtUtc": "2026-10-18T22:41:07.123Z",
  "receiptIssuedAtUtc": "2026-10-18T22:41:07.123Z",
  "verifyUrl": "https://<public origin>/verify?t=<token>"
}
```

| Field | Notes |
|---|---|
| `permitNumber` | The receipt number — which IS the permit number |
| `campus` | The college's canonical name from the fee catalog |
| `amountCents` | Exact amount in integer cents (3000 = $30.00) |
| `paypalCaptureId` | PayPal's capture id, or `null` for a $0 free-issued permit (no PayPal call exists) |
| `paidAtUtc` | UTC instant the capture completed, or `null` for a free-issued permit (issued, not paid) |
| `receiptIssuedAtUtc` | UTC instant the permit number/token were minted at issuance |
| `verifyUrl` | `{origin}/verify?t={token}`, or `null`. Origin rule: an explicit caller-supplied request origin wins; with none (the checkout capture path), the configured `Webhook:PublicBaseUrl` is the fallback; with neither, `verifyUrl` is `null` — an origin is never invented |

Configuration (both keys — see `docs/zapier-recipe.md` for the full
side-by-side): `Webhook:Url` is the destination (empty = disabled,
the default; env var `WEBHOOK_URL` also feeds it);
`Webhook:PublicBaseUrl` is the optional fallback public origin above.

## Error model

JSON errors share one shape: `{ "error": "<plain-words message>" }` —
with two stated exceptions: a tripped verify rate limit on the HTML
door answers plain text, and the inbound PayPal webhook answers a
status code with no JSON body.

| Status | Where | Meaning |
|---|---|---|
| 400 | orders, capture, extract, webhook | The request itself is unusable (empty message, empty order id, unchargeable stored price, no order to capture, malformed / id-less webhook delivery) |
| 401 | webhook | Unverifiable delivery (see that endpoint). Nothing is saved |
| 403 | orders, capture | The named application/order belongs to a different demo person |
| 404 | orders, capture | The named application/order does not exist |
| 429 | verify, api/verify | **Rate limit:** more than 60 permit checks/minute from this connection. JSON door: `{ "error": "Too many permit checks from this connection — please wait a minute and try again." }`; HTML door: the same words as plain text |
| 502 | orders, capture | PayPal's sandbox API errored. The permit is **not** paid; the message says exactly that |
| 503 | orders, capture | **Not configured:** the sandbox client id/secret are not set on this machine. The message names the missing settings (`PAYPAL_CLIENT_ID (PayPal:ClientId)` / `PAYPAL_CLIENT_SECRET (PayPal:ClientSecret)`) and never contains a credential value. This is a designed, honest state — a fresh clone runs this way until the developer creates a free sandbox app and sets the keys (see the repo README) — not a crash |

## Money rules (context any generated client must respect)

1. **Paid is decided in exactly two places, both server-side:** the
   capture handling in `CheckoutService`, and the verified inbound
   PayPal webhook handler — each only from a capture whose status is
   `COMPLETED` (webhook: plus the amount-match rule above).
   Return/success pages never mark anything paid.
2. **$0 is real and distinct:** a college whose published fee is $0
   (e.g. the YCCD campuses) gets a live permit with payment status
   `NotRequired` and **zero PayPal calls** — never a fake $0
   "payment". An Employee-category application is likewise exempt at
   checkout start: `NotRequired`, issued, no PayPal call.
3. **No listed price is never $0:** a campus whose fee is not
   published cannot be charged an invented amount; the catalog
   returns "not published", never 0 — and a stored price that
   disagrees with the catalog makes checkout refuse (400), not
   guess.
4. **Amounts are integer cents server-side** (`PriceCents`), formatted
   to exact USD dollars only at the PayPal boundary — $229.50 is sent
   as `"229.50"`, never rounded.
