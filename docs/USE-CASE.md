# Solvia — Use Case

**One student. One permit. One payment.**

Maria is a fictional student. All data in this demo is fictional, and all money is PayPal Sandbox test money.

## The flow, step by step

```
MARIA — student, first day
        |
        |  "I need a parking permit."
        v
1. PICK A COLLEGE
   Maria picks her college from the fee table.
   (AG Grid table; fees come from data/fees.csv —
    real fees scanned from 25 campuses, 2026-10-18.)
        |
        v
2. AI ASKS — 3 questions only
   School?  Car?  Term?
   (Phi-4-mini, a simple open model, runs locally via Ollama.
    It extracts her answers as function arguments —
    no free-text guessing.)
        |
        v
3. AI CHECKS + SHOWS THE EXACT TOTAL
   The AI looks up her college + term in the fee table
   (Elastic search) and shows the total BEFORE she pays.
   If her college does not publish a price, the app says so —
   it never invents a price.
        |
        v
4. MARIA SAYS YES
   She reviews the total and the permit dates
   (Bryntum calendar: term start, permit end, renewal date).
   No approval, no payment.
        |
        v
5. PAYPAL PAYS — Sandbox
   PayPal Orders flow: create order -> Maria approves on the
   PayPal Sandbox page with a test buyer account -> capture.
   Result: COMPLETED, with a transaction ID.
        |
        v
6. DONE
   Receipt sent (Zapier step: receipt + renewal reminder).
   Permit is live. Maria can park.
```

## AI model choice

**AI model choice: Microsoft Phi-4-mini — a U.S. (Microsoft) open model, MIT license, 2.5 GB, runs 100% locally on the student's own computer via Ollama. Country rule for this project: no China tools, models, or services, and no Russia tools, models, or services — anywhere in the stack; only those two are out. Qwen was evaluated and not used because Qwen is a China model. Because the model runs locally, student data never leaves the student's computer and is never used to train any model.**

## Why this matters

- Today, at the incumbent store (mycampuspermit.com), many campuses do not publish their semester price on the public page at all — in the 2026-10-18 scan, only 10 of 25 campuses listed a student semester price publicly; the rest hide it behind a login or do not list it. A student can finish a long form without knowing the total.
- Prices also differ a lot by college (publicly listed semester prices in the scan run from FREE to $229.50), so one generic price would be wrong. The total must come from her college's real row.
- Maria's case fixes both: the price comes first, from her college, and she only approves — the AI and PayPal do the steps.

## What Maria never does

- Fill a long form.
- Guess a price.
- Pay real money in this demo (Sandbox only).
- Share real personal data (the demo student is fictional).
