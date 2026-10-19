# Postman — Solvia's PayPal sandbox flow, as our app exposes it (REDO)

This folder is the WORKFLOW-sponsor proof for **Postman**: a real,
runnable collection that tests the PayPal sandbox flow **through
Solvia's own API** — the exact endpoints the app's checkout page
calls — instead of pointing Postman at PayPal directly.

| File | What it is |
|---|---|
| `solvia-paypal-flow.collection.json` | Postman collection (schema v2.1): 1 setup request + the 4 flow requests, each with a test script asserting the response shape |
| `solvia-local.environment.json` | Matching local environment — variables only, **no secrets** (Solvia never takes PayPal keys through Postman; the app reads them from its own environment) |

## What the collection proves

That Solvia's money rules hold at its own API door:

- The amount is never in a request. Create-order sends only a permit
  application id; the server charges that application's snapshotted
  `PriceCents` — the college's real published fee from `data/fees.csv`.
- Paid is decided in exactly one place, server-side: `completed` is
  true **only** when PayPal's capture answered `COMPLETED`, and then a
  capture id and a receipt (permit) number must both be present.
- The answers stay honest when things are missing: no sandbox keys →
  a clear 503 naming the missing settings (never a value); no order →
  capture stops at 400/404 before PayPal is ever called; an unapproved
  order → capture is refused (502) and the permit is **not** paid.
- Verification is anonymous and stingy: the JSON verify answer
  carries permit facts only — never the plate itself (state only),
  never a student name or email.
- The AI draft never carries a price, and its `source` field says
  plainly which path answered.

## The requests, in order

0. **Setup — get the antiforgery token** —
   `GET /Checkout/{permitApplicationId}`.
   The redo's two PayPal endpoints are protected by an antiforgery
   check (in code: `[ValidateAntiForgeryToken]` on
   `POST /api/paypal/orders` and `POST /api/paypal/capture`). In plain
   words: before the app will take a PayPal call, the call must prove
   it came from a page the app itself served. The proof has two
   halves that belong together:

   - a **cookie** the app sets when it serves the checkout page, and
   - a **token value** printed inside that page, in the hidden
     `__RequestVerificationToken` field of the plain-pay form.

   That is exactly how the checkout page's own buttons work: its
   script reads the token out of that form and sends it as the
   `RequestVerificationToken` header, and the browser sends the
   cookie on its own. This setup request copies that move — it loads
   the checkout page, Postman's cookie jar keeps the cookie, and the
   request's test script saves the token into the `antiforgeryToken`
   variable for requests 1 and 2. **Leave the cookie jar on and run
   the requests in order**; a token from one page load will not match
   a cookie from another. (The checkout page only renders for an
   unpaid application priced above $0 — a paid or $0 permit redirects
   to My Permits and has no token to take.) The other two endpoints
   need no token: verify is a GET, and extract is a plain JSON
   endpoint.
1. **Create order** — `POST /api/paypal/orders`
   `{ "permitApplicationId": "..." }` →
   `{ "id": "<order id>", "status": "created" }`.
   (This is the REDO route; the frozen v1 build called it
   `/api/paypal/create-order` and answered a `free` flag instead of
   `status`.) Create is idempotent — a repeat call returns the stored
   order, never a second one. An application that needs no payment
   answers `{ "id": null, "status": "not-required" }` with no PayPal
   call at all; an already-Paid one answers
   `{ "id": "<stored order id>", "status": "already-paid" }`.
   An unknown id answers 404
   `{ "error": "That permit application was not found." }`, another
   demo person's answers 403 — both before PayPal is consulted. On
   success the test script saves the order id into the
   `paypalOrderId` environment variable for request 2.
2. **Capture** — `POST /api/paypal/capture` `{ "orderId": "..." }` →
   `{ "status", "completed", "captureId", "receiptNumber" }`.
3. **Verify a permit** — `GET /api/verify?t=<token>` → the public,
   anonymous verification answer as JSON: the same field set the
   `/verify` page renders (`found`, `isValid`, `reason`,
   `permitNumber`, `campus`, `permitType`, `plateState`, `category`,
   `privilege`, `paymentStatus`, `startDate`, `endDate`,
   `lastUpdatedUtc`). An unknown or empty token still answers a calm
   HTTP 200 with `found: false` and every permit field null — never a
   500, never a hint about other permits. A shared limit of 60 checks
   per minute per connection guards this door; past it the answer is
   429 with a small `{ "error": ... }` body.
4. **AI extraction** — `POST /api/chat/extract` `{ "text": "..." }` →
   the validated permit draft (`college`, `permitType`, `term`,
   `carMake`, `carModel`, `carColor`, `plate`, `carState`, `source`).
   The test script asserts `source` is honestly `"ollama"` (local
   Phi-4-mini answered) or `"fallback"` (deterministic extractor
   answered) — and that the draft never carries a price.

## The one human step no API test can do for you

Between create (request 1) and capture (request 2), **the buyer
approval happens on `sandbox.paypal.com`, and only a person can do
it.** In the app that is the browser checkout flow — the PayPal
buttons, or the plain redirect — signed in with a **sandbox buyer
account** (developer.paypal.com → Sandbox → Accounts), clicking
Approve / Pay on PayPal's own page. No API call can approve an order;
Postman cannot do this step, and neither can Solvia's server.

So, run honestly, the collection has two shapes:

- **With sandbox keys + a human approval in the middle:** request 1
  returns a real order id, you approve it in the browser flow, then
  request 2 captures it and asserts `completed: true` only against a
  `COMPLETED` status with a capture id and a receipt number.
- **Without that click** (or without keys): capturing an order
  nobody approved does **not** mark anything paid. PayPal refuses it
  and Solvia answers 502
  `{ "error": "PayPal sandbox could not capture the order. The permit is not paid." }`
  — or, if PayPal returns a non-COMPLETED status instead, 200 with
  `completed: false`. The capture test script asserts whichever of
  these honest shapes came back instead of hiding it.

## Before you run: a free PayPal sandbox app

Solvia talks to the PayPal **sandbox only** — fictitious accounts, no
real money. Create a free sandbox app once:

1. Go to <https://developer.paypal.com> and sign in (or sign up free).
2. **Dashboard → My Apps & Credentials → Sandbox → Create App.**
3. Copy the app's **Client ID** and **Secret** into the place Solvia
   reads them from — environment variables `PAYPAL_CLIENT_ID` /
   `PAYPAL_CLIENT_SECRET`, or .NET user-secrets
   (`PayPal:ClientId` / `PayPal:ClientSecret`; see the repo README's
   "Run it locally"). **Never** paste them into Postman, this folder,
   or any committed file.

**Running without keys is a valid test too.** With no sandbox keys
configured:

- request 1 answers the app's honest **503**:
  `{ "error": "PayPal sandbox credentials are not configured — missing: PAYPAL_CLIENT_ID (PayPal:ClientId), PAYPAL_CLIENT_SECRET (PayPal:ClientSecret). ..." }`
  — it names the missing settings, never a value;
- request 2 then has no order to capture (`paypalOrderId` is empty),
  so it stops at **400** `{ "error": "An order id is required." }`
  before PayPal is called — or, if you type an order id no
  application owns, at **404**
  `{ "error": "No permit application owns that PayPal order." }`;
- requests 3 and 4 answer normally (verify: 200 JSON; extract: 200,
  `source: "fallback"` when Ollama is not running).

The test scripts assert each of those shapes instead of failing.

## Import + run

1. Start Solvia locally (repo README): `dotnet run --project src/Solvia`
   serves it at `http://localhost:5156` (the environment's default
   `baseUrl`; if you start it on another port, change `baseUrl` to
   match).
2. In Postman: **Import** → drop in both JSON files from this folder.
3. Select the **"Solvia local — sandbox app"** environment, and leave
   Postman's **cookie jar enabled** (Settings → General) — request 0's
   antiforgery cookie has to ride along on requests 1 and 2.
4. Fill in two variables (Environments → the imported environment):
   - `permitApplicationId` — run Maria's flow in the browser once
     (apply → … → checkout); the checkout page URL is
     `/Checkout/{permitApplicationId}` — copy that GUID. The permit
     must be **unpaid and priced above $0**, or request 0 has no
     checkout page (and no token) to load.
   - `verifyToken` — after a payment (or a $0 free issue), the receipt
     page prints a verify link `/verify?t=<token>` — copy the token.
     (Request 3 runs without it too: an empty token gets the neutral
     `found: false` answer, still HTTP 200.)
   `paypalOrderId` and `antiforgeryToken` are filled in for you by
   requests 0 and 1.
5. Run the requests **in order, in one session**, or open the
   collection's **Run** view and run all five. Every request carries
   its own test script; the run is green when the shapes above hold —
   including in the no-keys run, where the 503/400 shapes are the
   expected, asserted answers.
