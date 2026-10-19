# Opt-in PayPal Sandbox matrix

`tests/sandbox/paypal-matrix.spec.ts` runs the real application at
`http://localhost:5156` against PayPal's actual Sandbox REST API. It is separate
from the isolated `tests/e2e` suite and never runs through `npm run test:e2e`.
The matrix definition describes intended coverage; only completed generated
evidence proves that a case was executed successfully.

## Coverage

Maria is the fictional Student and Pat is the fictional Public buyer. Each
tries a Fall term permit and a day pass at these campuses. The assertions use
the checked-in CSV fixture, not a live college feed.

| Campus | Term, USD | Day, USD |
|---|---:|---:|
| CSU Monterey Bay | 229.50 | 5.00 |
| CSU East Bay | 195.00 | 10.00 |
| Fresno City College | 30.00 | 1.00 |
| Modesto Junior College / Columbia College / YCCD | 0.00 | 2.00 |

This gives 16 student/public cases. Four more cases check Elena's Employee
exemption for both permit types and blocked unpublished CSU Bakersfield
Summer pricing for both Maria and Pat. The exemption is the demo policy,
not a claim about every institution's employee eligibility. The YCCD free
term outcome for Public is likewise the current demo pricing behavior.

An entire run creates **14 actual Sandbox payments**, with a combined gross
test amount of **$945.00 USD**, and no real-money transactions. Two YCCD term
permits and two employee permits issue without a PayPal payment. The two
unpublished-price applications remain blocked. A pilot plus the remaining
cases is one matrix, not a reason to buy the pilot again.

Paid cases check the fee shown before checkout, persisted application and
plate, server order creation, order retry reuse, exact provider amount and
USD currency, expected Sandbox merchant, completed capture, capture retry
reuse, and exactly one provider capture. They then check the actual receipt,
PDF response, and public permit-verification page. The latter uses a separate
API context without the buyer cookie and checks that the plate is absent.
The receipt and PDF routes continue to use the buyer's own cookie.

## Run locally

Install dependencies and Chromium, and start the normal development app
with its existing database on port 5156. Use a dedicated demo database if
you do not want the new fictional applications in your preview. This config
does not start a server, change fees, or reset the database.

Configure the app for Sandbox through its local secret configuration. Supply
these variables to the test process through your local credential manager
or private process environment; do not paste credential values into shell
history, source files, Devpost, screenshots, or Git:

- `PAYPAL_SANDBOX_CLIENT_ID`: the same Sandbox app used by Solvia.
- `PAYPAL_SANDBOX_CLIENT_SECRET`: that app's private Sandbox secret.
- `PAYPAL_SANDBOX_MERCHANT_ID`: the expected Sandbox Business seller's merchant ID.

The runner verifies the order's merchant before confirming its test-card
payment source. Its provider URL is fixed to
`https://api-m.sandbox.paypal.com`; it offers no live-mode option.

```powershell
npm ci
npx playwright install chromium
npm run typecheck
$env:SOLVIA_SANDBOX_TESTS = '1'
npx playwright test --config playwright.sandbox.config.ts
Remove-Item Env:SOLVIA_SANDBOX_TESTS
```

The suite is skipped unless the explicit opt-in variable equals `1`. Run a
single pilot with `--grep 'CSU Monterey Bay.*Student.*term'`. A complementary
`--grep-invert 'CSU Monterey Bay.*Student.*term'` runs the remaining cases.
Use an unanchored expression because Playwright matches the complete test
title, including its file. Fall permit-validity assertions are date-sensitive:
run within the fixture's Fall validity window or deliberately update the
term/date fixtures and expected pricing first.

Runs use one worker and **zero automatic retries**. Re-running a case creates
a new application, plate, order, and payment. If a run stops after capture,
inspect its saved references and PayPal before deciding what to rerun.

## Evidence and limits

Ignored `sandbox-results/` contains Playwright's JSON report, failure
screenshots, and per-case sanitized JSON/PDF evidence. Progress JSON is
written after order creation and capture so a later receipt/PDF failure
does not hide a completed provider transaction. `status: IN_PROGRESS` is a
partial case, not a completed matrix check. Reports and files are overwritten
on later runs; archive a completed run separately when preserving evidence.

Browser traces are disabled. Provider transport errors are replaced with a
sanitized message before request headers or test-card request bodies can
enter the report. Evidence records fictional names/plates, amounts, application
IDs, order/capture IDs, receipt numbers, and check outcomes. It contains no
client secret or access token. Avoid uploading raw process logs or local
credential files.

The runner uses PayPal's published **Sandbox test card** through
`confirm-payment-source`, followed by Solvia's real `POST /api/paypal/capture`
endpoint and an independent provider query. Solvia does not collect raw card
data in its product UI. This proves the server order/capture path with test
money; it does **not** prove hosted wallet-button approval, Personal-buyer
login, every card type, a live merchant account, refunds, or webhook delivery.
`walletUiApprovalVerified` stays false in each evidence record. Test the hosted
wallet path separately using a Personal Sandbox buyer rather than the
Business seller account.

Useful official references:

- [PayPal Sandbox accounts](https://developer.paypal.com/sandbox-testing/accounts)
- [Orders v2: confirm payment source](https://developer.paypal.com/api/orders/v2/orders-confirm)
- [PayPal testing cards](https://developer.paypal.com/credit-card-number-generator)

The demo-person switcher is not production authentication. Real AI inference
is also a separate check: this matrix accepts the clearly labeled built-in
fallback and must not be presented as proof that Phi-4-mini ran.
