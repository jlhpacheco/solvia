# Postman — Solvia's PayPal sandbox flow, as our app exposes it

This folder is the WORKFLOW-sponsor proof for **Postman** (see
`docs/SPONSORS.md`): a real, runnable collection that tests the PayPal
sandbox flow **through Solvia's own API** — the exact endpoints the
app's checkout page calls — instead of pointing Postman at PayPal
directly.

| File | What it is |
|---|---|
| `solvia-paypal-flow.collection.json` | Postman collection (schema v2.1): 4 requests, each with a test script asserting the response shape |
| `solvia-local.environment.json` | Matching local environment — variables only, **no secrets** (Solvia never takes PayPal keys through Postman; the app reads them from its own environment) |

The 4 requests:

1. **Create order** — `POST /api/paypal/create-order`
   `{ "permitApplicationId": "..." }` → `{ "id": "<order id>" }`.
   The amount is never in the request: the server uses the permit's
   snapshotted `PriceCents` (the college's real fee from
   `data/fees.csv`). A $0 FREE permit answers `{ "id": null, "free": true }`
   with no PayPal call. On success the test script saves the order id
   into the `paypalOrderId` environment variable for request 2.
2. **Capture** — `POST /api/paypal/capture` `{ "orderId": "..." }` →
   `{ "status", "completed", "captureId", "receiptNumber" }`.
   The test script asserts the app's core money rule: `completed` is
   true **only** when `status` is `COMPLETED`, and then a capture id
   and a receipt/permit number must both be present.
3. **Verify a permit** — `GET /verify?t=<token>` → the public,
   anonymous verification page (HTTP 200, VALID or a calm NOT VALID
   with a plain-words reason).
4. **AI extraction** — `POST /api/chat/extract` `{ "text": "..." }` →
   the validated permit draft. The test script asserts `source` is
   honestly `"ollama"` (local Phi-4-mini answered) or `"fallback"`
   (deterministic extractor answered) — and that the draft never
   carries a price.

## Before you run: a free PayPal sandbox app

Solvia talks to the PayPal **sandbox only** — fictitious accounts, no
real money. Create a free sandbox app once:

1. Go to <https://developer.paypal.com> and sign in (or sign up free).
2. **Dashboard → My Apps & Credentials → Sandbox → Create App.**
3. Copy the app's **Client ID** and **Secret** into the place Solvia
   reads them from — environment variables `PAYPAL_CLIENT_ID` /
   `PAYPAL_CLIENT_SECRET`, or .NET user-secrets (see the repo README's
   "Run it locally"). **Never** paste them into Postman, this folder,
   or any committed file.

**Running without keys is a valid test too:** with no sandbox keys
configured, requests 1–2 answer the app's honest **503**
(`{ "error": "...PAYPAL_CLIENT_ID..." }` — it names the missing
setting, never a value), and the collection's test scripts assert
that shape instead of failing.

## Import + run

1. Start Solvia locally (repo README): `dotnet run --project src/Solvia`
   serves it at `http://localhost:5156` (the collection's default
   `baseUrl`; if you start it with `--urls http://localhost:5187`,
   change the environment's `baseUrl` to match).
2. In Postman: **Import** → drop in both JSON files from this folder.
3. Select the **"Solvia local — sandbox app"** environment.
4. Fill in two variables (Environments → the imported environment):
   - `permitApplicationId` — run Maria's flow in the browser once
     (apply → review → confirm); the checkout page URL is
     `/Checkout/{permitApplicationId}` — copy that GUID.
   - `verifyToken` — after a payment (or a $0 free issue), the receipt
     page prints a verify link `/verify?t=<token>` — copy the token.
     (Request 3 runs without it too: an empty token gets the neutral
     NOT VALID page, still HTTP 200.)
5. Run the requests in order, or open the collection's **Run** view
   and run all four. Every request carries its own test script; the
   run is green when the shapes above hold.

**The one human step no API test can do for you:** between create and
capture, the buyer approval happens on `sandbox.paypal.com`. In the
app that is the browser checkout flow (PayPal buttons, or the redirect
flow) with a **sandbox buyer account** (developer.paypal.com →
Sandbox → Accounts). Capturing an order nobody approved honestly
answers `completed: false` — the tests assert exactly that behavior
instead of hiding it.
