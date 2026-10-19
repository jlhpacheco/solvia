# Sandbox payment status and recovery

Choose the same fictional person who created the application on Home,
then open My permits and Finish paying. Checkout displays the snapshotted
college fee, existing PayPal order ID, and capture ID or pending text.

An order ID identifies the request to collect that fee. It is not a receipt.
For wallet checkout, the buyer approves the order with a separate
**Personal sandbox test account**. The configured **Business sandbox
account** is the seller and cannot buy from itself. Use sandbox test funds;
no real card is needed. Official Sandbox test cards are another test-only
payment route; the October 19 API check below used that route.
Retrieve the Personal account privately in the developer dashboard's sandbox
accounts area. Never add its password or app secrets to source or reports.

- `ORDER_NOT_APPROVED`: approve the existing order using the Personal buyer.
- `PENDING`: payment has not completed. A capture ID may exist, but the
  permit is not active until completion and exact amount/currency validation.
- `ORDER_ALREADY_CAPTURED`: Solvia retrieves the provider's order details
  and reconciles its actual capture; it does not create another order.
- Timeout/network error: check the permit's current status before retrying.
  A missing response does not prove the provider did nothing.
- Buttons fail to load: use the plain PayPal redirect button. Repeating it
  resumes the same order through its current provider approval link.
- Free/exempt permit: no PayPal IDs are required and no payment is taken.

Both payment endpoints require the page's antiforgery token and the current
demo owner. `POST /api/paypal/orders` returns the durable order ID.
`POST /api/paypal/capture` returns the order ID, capture status/ID and
`completed`. Browser code accepts success only when `completed` is true,
status is `COMPLETED`, and a capture ID exists. The server independently
checks the exact fee and USD before issuance. HTTP failures expose a safe
provider issue code/support reference when available.

## October 19 provider result

The initial $30.00 USD order returned `CREATED` and
`ORDER_NOT_APPROVED`; redirect resume reused that same order. A later
test completed its official Sandbox test-card confirmation and invoked
Solvia's actual `POST /api/paypal/capture` endpoint successfully:

- Order: `1G1852006N6969449`, independently confirmed `COMPLETED`.
- Capture: `7WR24202DB5257942`, independently confirmed `COMPLETED` for
  exactly $30.00 USD.
- Receipt: `SOL-2026-197973`, with both provider IDs in the paid PDF.

This is actual provider Sandbox evidence, separate from simulated SDK tests.
Wallet/Personal-buyer UI approval remains unverified. The seller cannot pay
itself; a `CANNOT_PAY_SELF` message requires a different sandbox buyer session.

Fees come from the accepted CSV catalog. Solvia creates a distinct order for
each paid application using its snapshotted total; it does not need a hosted
payment link for each college or price. A later CSV edit must not change an
open order's agreed amount.

A follow-up matrix passed 20 real-integration checks across four campuses:
14 paid captures totaling $945.00 USD in Sandbox test funds, two free permit
cases, and four guards. All 16 permit PDFs passed content validation. See
[the detailed results](SANDBOX-RESULTS-2026-10-19.md) and
[matrix instructions](SANDBOX-MATRIX.md).

References: [Sandbox accounts](https://developer.paypal.com/sandbox-testing/accounts),
[Orders API](https://developer.paypal.com/api/orders/v2),
[HTTP responses and idempotency](https://developer.paypal.com/api/rest/responses/).
