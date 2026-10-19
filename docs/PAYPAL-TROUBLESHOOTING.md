# Sandbox payment status and recovery

Choose the same fictional person who created the application on Home,
then open My permits and Finish paying. Checkout displays the snapshotted
college fee, existing PayPal order ID, and capture ID or pending text.

An order ID identifies the request to collect that fee. It is not a receipt.
The buyer must approve the order with a separate **Personal sandbox test
account**. The configured **Business sandbox account** is the seller and
cannot buy from itself. Use sandbox test funds; no real card is needed.
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

On October 19, a real Sandbox order was confirmed `CREATED` for $30 USD,
the app returned `ORDER_NOT_APPROVED` on capture, and redirect resume reused
the same order. Completed buyer approval/capture is still an outstanding
integration check. Automated SDK contract tests use simulated responses and
are not a substitute for that check.

References: [Sandbox accounts](https://developer.paypal.com/sandbox-testing/accounts),
[Orders API](https://developer.paypal.com/api/orders/v2),
[HTTP responses and idempotency](https://developer.paypal.com/api/rest/responses/).
