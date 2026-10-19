# Zapier — the payment-complete receipt Zap (recipe)

**What is real, and what is an account step.** The **app side of this
flow is built in the redo tree**: when Solvia marks a permit Paid (or
confirms a $0 FREE permit), it POSTs one small JSON event to a
configurable webhook URL — `src/Solvia/Services/PaymentWebhookService.cs`,
fired after the save by `CheckoutService` (capture and free-issue
paths) and by `PayPalWebhookHandler` (verified PayPal webhook path).
The payload shape and the verify-URL origin rules below are covered
by `tests/Solvia.Tests/WebhookTests.cs` (payload carries permit facts
and no student PII; the three origin cases are tested explicitly).

The **Zap itself is created in Jose Luis's own Zapier account** —
that is an account step only he can do, and **no live Zap exists, and
none is claimed, until he builds it** from this recipe. Nothing in
this repo says "connected to Zapier"; it says the app emits the event
and this document maps it into a Zap.

## The event the app sends

POSTed as JSON to the configured URL the moment a permit goes live
(after the payment truth is saved — never before, never instead).
The field list below matches `PaymentWebhookService.cs` **exactly** —
seven fields, in the order the code builds them:

```json
{
  "permitNumber": "SOL-2026-483920",
  "campus": "Fresno City College",
  "amountCents": 3000,
  "paypalCaptureId": "9XY…",
  "paidAtUtc": "2026-10-18T22:41:07.123Z",
  "receiptIssuedAtUtc": "2026-10-18T22:41:07.123Z",
  "verifyUrl": "https://solvia.example.com/verify?t=TOKEN-ABC"
}
```

| Field | Meaning |
|---|---|
| `permitNumber` | The receipt number — which IS the permit number (`PermitApplication.ReceiptNumber`) |
| `campus` | The college's canonical name from the fee catalog (`PermitApplication.Campus`) |
| `amountCents` | Exact amount in integer cents (3000 = $30.00) (`PermitApplication.PriceCents`) |
| `paypalCaptureId` | PayPal's capture id — the transaction proof. `null` for a $0 FREE permit (no PayPal call exists) |
| `paidAtUtc` | UTC instant the capture completed. `null` for a $0 FREE permit (it was issued, not paid) |
| `receiptIssuedAtUtc` | UTC instant the permit was minted — receipt number, verify token, and this stamp are created together at issuance |
| `verifyUrl` | The public permit link, `{origin}/verify?t={token}` — or `null` when no origin is available (see the origin rule under Configure, below). An origin is **never invented** |

**Privacy, by design:** the event carries **no student PII** — no
name, no email, no plate. That is deliberate (see `docs/PRIVACY.md`):
the webhook announces permit facts only. A consequence to know: the
receipt email's recipient is configured **in the Zap** (step 3), not
taken from the event.

**It never blocks a payment.** The send is fire-and-forget in effect:
it never throws, it is capped at 3 seconds, and every failure
(endpoint down, non-2xx, timeout) is caught and logged — the
already-saved permit is unaffected either way. It fires **only** on
Paid / free-issue. A failed or abandoned checkout fires nothing.

## Configure the app side (both keys, side by side)

Point Solvia at the Zap's Catch Hook URL (created in step 1 below),
and — if you want the capture path's events to carry the public
verify link — set the public origin fallback too. Both keys live in
the `Webhook` section, bound by `WebhookOptions` in
`src/Solvia/Config/AppOptions.cs`:

| Key | What it does | Default | How to set it |
|---|---|---|---|
| `Webhook:Url` | **The destination.** The URL Solvia POSTs the payment-complete event to — the Zap's Catch Hook URL. | `""` (empty = **disabled**: no event is sent, the app behaves exactly as before) | `appsettings.json`: `"Webhook": { "Url": "<the catch-hook URL>" }`, or environment variable `WEBHOOK_URL=<the catch-hook URL>` (bridged to `Webhook:Url` in `Program.cs`; the standard `Webhook__Url` form also works) |
| `Webhook:PublicBaseUrl` | **The verify-link origin fallback.** The app's public origin (scheme + host, e.g. `https://solvia.example.com`), used to build the event's `verifyUrl` when the caller has no request origin of its own. | `""` (empty = no fallback) | `appsettings.json`: `"Webhook": { "PublicBaseUrl": "https://solvia.example.com" }` (config only — there is no dedicated env-var bridge for this key; the standard `Webhook__PublicBaseUrl` environment form works through normal .NET configuration) |

Restart the app after changing either key.

### The verifyUrl origin rule (exactly how the fallback works)

`verifyUrl` is `{origin}/verify?t={token}`, and the origin is chosen
in this order — implemented in `PaymentWebhookService.cs` and proven
by the three `OutboundVerifyUrl_*` tests in `WebhookTests.cs`:

1. **Explicit request origin wins.** A caller that holds an HTTP
   request passes its own origin (`{scheme}://{host}`). Today that is
   the inbound PayPal webhook path: `PayPalWebhookController` builds
   the origin from its request and `PayPalWebhookHandler` passes it
   through. When it is present, `Webhook:PublicBaseUrl` is ignored.
2. **Config is the fallback.** The checkout capture path has **no
   request to read an origin from** — `CheckoutService` calls the
   webhook service with no origin (`null`). The service then falls
   back to the configured **`Webhook:PublicBaseUrl`**, and that is
   what lets the capture path's payload still carry the public
   `verifyUrl`.
3. **Neither → `verifyUrl` is `null`.** With no caller origin and no
   configured fallback, the event is still sent (when `Webhook:Url`
   is set) — it simply carries `"verifyUrl": null`. The app never
   invents an origin, a host, or a link.

## Build the Zap (in Jose Luis's Zapier account)

### Step 1 — Trigger: Catch Hook

1. Zapier → **Create Zap** → Trigger: **Webhooks by Zapier** →
   **Catch Hook** → Continue (no child key needed).
2. Zapier shows a **webhook URL** — copy it into Solvia's
   `Webhook:Url` / `WEBHOOK_URL` (above). If the app will be reached
   at a public origin, also set `Webhook:PublicBaseUrl` to that
   origin so capture-path events carry the verify link. Restart the
   app.
3. Make one real test event: in Solvia, run a $0 FREE permit (pick
   the YCCD campus — the flow completes with no PayPal step) or a
   sandbox payment. Back in Zapier, **Test trigger** → the event
   arrives with the seven fields above. Pick it as the trigger
   sample.

### Step 2 — (Optional, recommended) Formatter: cents → dollars

1. Action: **Formatter by Zapier** → **Numbers** → **Perform
   Math Operation**: `amountCents` **Divide** `100`.
2. Name the output `amountDollars` in your head — steps 3–4 use it so
   humans see `$30` instead of `3000`. Skip this step and the ledger
   still works; it just records raw cents.

### Step 3 — Action: Gmail receipt to the student

1. Action: **Gmail** → **Send Email** (connect Jose Luis's Gmail in
   Zapier when prompted — that connection lives in his Zapier
   account, never in this repo).
2. **To:** the mailbox configured for the demo. Because the event
   intentionally carries no email address, set the recipient here:
   for the demo, Jose Luis's own test inbox standing in for Maria's;
   in a real deployment this Zap would be preceded by a lookup step
   keyed on the permit number.
3. **Subject:** `Your Solvia permit {{permitNumber}} — {{campus}}`
4. **Body:**

   ```
   Your campus parking permit is live.

   Permit number:  {{permitNumber}}
   College:        {{campus}}
   Amount paid:    ${{amountDollars}}  (PayPal capture: {{paypalCaptureId}})
   Paid at (UTC):  {{paidAtUtc}}
   Issued at (UTC): {{receiptIssuedAtUtc}}
   Verify it here: {{verifyUrl}}

   Keep the permit number — campus officers can verify it with the
   verify link above, or with the link on your Solvia receipt page.
   ```

   For a $0 FREE permit, `paypalCaptureId` and `paidAtUtc` arrive
   empty — the body still reads correctly ("Amount paid: $0"). If
   `verifyUrl` arrives empty, no origin was available when the event
   fired — set `Webhook:PublicBaseUrl` (above) and the next event
   will carry the link; the receipt page's own verify link works
   either way.

### Step 4 — Action: Google Sheets ledger row

1. Action: **Google Sheets** → **Create Spreadsheet Row**.
2. Pick (or create) the ledger spreadsheet with these columns, and
   map each column to the event field:

   | Ledger column | Mapped from |
   |---|---|
   | Paid at (UTC) | `paidAtUtc` |
   | Issued at (UTC) | `receiptIssuedAtUtc` |
   | Permit number | `permitNumber` |
   | Campus | `campus` |
   | Amount (cents) | `amountCents` |
   | Amount ($) | Formatter output `amountDollars` (step 2) |
   | PayPal capture ID | `paypalCaptureId` |
   | Verify URL | `verifyUrl` |

3. **Test the Zap** with the sample event, confirm the row lands,
   then **Publish**.

## Field mapping, all in one place

| Event field | Gmail receipt | Sheets ledger |
|---|---|---|
| `permitNumber` | Subject + body | "Permit number" column |
| `campus` | Subject + body | "Campus" column |
| `amountCents` | — (via Formatter → $) | "Amount (cents)" column |
| `paypalCaptureId` | Body (transaction proof) | "PayPal capture ID" column |
| `paidAtUtc` | Body | "Paid at (UTC)" column |
| `receiptIssuedAtUtc` | Body | "Issued at (UTC)" column |
| `verifyUrl` | Body (verify link) | "Verify URL" column |

## If no event arrives

- `Webhook:Url` / `WEBHOOK_URL` still empty? The webhook is disabled
  by design — set it and restart the app.
- URL set but nothing fires? Only a **completed** payment or a
  **free issue** fires. A failed or abandoned checkout fires nothing —
  that is the rule, not a bug.
- Event sent but `verifyUrl` is empty? No origin was available: the
  caller had no request origin and `Webhook:PublicBaseUrl` is unset.
  Set the fallback key and restart — origins are never invented, so
  this is the designed answer, not a lost link.
- Event sent but Zapier shows an error? Check the app log: Solvia
  logs every send ("Payment webhook sent…") and every failure
  ("…failed to send — the permit itself is unaffected"). The payment
  always stands either way; re-running the receipt email is a Zapier
  replay, never a re-charge.
