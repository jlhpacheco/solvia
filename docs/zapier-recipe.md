# Zapier — the payment-complete receipt Zap (recipe)

**What is real, and what is an account step.** The **app side of this
flow is built and unit-tested**: when Solvia marks a permit Paid (or
confirms a $0 FREE permit), it POSTs one small JSON event to a
configurable webhook URL — `Services/PaymentWebhookService.cs`, wired
in `CheckoutService`, proven by `tests/Solvia.Tests/WebhookTests.cs`
(fires on Paid and on free-issue; silent when unconfigured; a dead
endpoint can never block or roll back a payment).

The **Zap itself is created in Jose Luis's own Zapier account** —
that is an account step only he can do, and **no live Zap exists or is
claimed** until he builds it from this recipe. Nothing in this repo
says "connected to Zapier"; it says the app emits the event and this
document maps it into a Zap.

## The event the app sends

POSTed as JSON to the configured URL the moment a permit goes live
(after the payment truth is saved — never before, never instead):

```json
{
  "permitNumber": "SOL-2026-483920",
  "campus": "Fresno City College",
  "amountCents": 3000,
  "paypalCaptureId": "9XY…",
  "paidAtUtc": "2026-10-18T22:41:07.123Z"
}
```

| Field | Meaning |
|---|---|
| `permitNumber` | The receipt number — which IS the permit number |
| `campus` | The college's canonical name from the fee catalog |
| `amountCents` | Exact amount in integer cents (3000 = $30.00) |
| `paypalCaptureId` | PayPal's capture id — the transaction proof. `null` for a $0 FREE permit (no PayPal call exists) |
| `paidAtUtc` | UTC instant the capture completed. `null` for a $0 FREE permit (it was issued, not paid) |

**Privacy, by design:** the event carries **no student PII** — no
name, no email, no plate. That is deliberate (see `docs/PRIVACY.md`):
the webhook announces permit facts only. A consequence to know: the
receipt email's recipient is configured **in the Zap** (step 3), not
taken from the event.

## Configure the app side (one setting)

Point Solvia at the Zap's Catch Hook URL (created in step 1 below):

- environment variable: `WEBHOOK_URL=<the catch-hook URL>`, or
- `appsettings.json`: `"Webhook": { "Url": "<the catch-hook URL>" }`

Empty (the default) = disabled: no event is sent, and the app behaves
exactly as before. Restart the app after changing it.

## Build the Zap (in Jose Luis's Zapier account)

### Step 1 — Trigger: Catch Hook

1. Zapier → **Create Zap** → Trigger: **Webhooks by Zapier** →
   **Catch Hook** → Continue (no child key needed).
2. Zapier shows a **webhook URL** — copy it into Solvia's
   `Webhook:Url` / `WEBHOOK_URL` (above) and restart the app.
3. Make one real test event: in Solvia, run a $0 FREE permit (pick
   the YCCD campus — the flow completes with no PayPal step) or a
   sandbox payment. Back in Zapier, **Test trigger** → the event
   arrives with the five fields above. Pick it as the trigger sample.

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

   Keep the permit number — campus officers can verify it with the
   verify link on your Solvia receipt page.
   ```

   For a $0 FREE permit, `paypalCaptureId` and `paidAtUtc` arrive
   empty — the body still reads correctly ("Amount paid: $0").

### Step 4 — Action: Google Sheets ledger row

1. Action: **Google Sheets** → **Create Spreadsheet Row**.
2. Pick (or create) the ledger spreadsheet with these columns, and
   map each column to the event field:

   | Ledger column | Mapped from |
   |---|---|
   | Paid at (UTC) | `paidAtUtc` |
   | Permit number | `permitNumber` |
   | Campus | `campus` |
   | Amount (cents) | `amountCents` |
   | Amount ($) | Formatter output `amountDollars` (step 2) |
   | PayPal capture ID | `paypalCaptureId` |

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

## If no event arrives

- `Webhook:Url` / `WEBHOOK_URL` still empty? The webhook is disabled
  by design — set it and restart the app.
- URL set but nothing fires? Only a **completed** payment or a
  **free issue** fires. A failed or abandoned checkout fires nothing —
  that is the rule, not a bug.
- Event sent but Zapier shows an error? Check the app log: Solvia
  logs every send ("Payment webhook sent…") and every failure
  ("…failed to send — the permit itself is unaffected"). The payment
  always stands either way; re-running the receipt email is a Zapier
  replay, never a re-charge.
