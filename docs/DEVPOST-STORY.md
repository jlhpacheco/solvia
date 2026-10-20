## Inspiration

Getting to class should not require deciphering a parking portal. A student or visitor needs three clear answers: which permit fits, what it costs, and whether payment really completed. Solvia brings those steps into one mobile-friendly journey, built in San Jose.

## What it does

Describe your car, review the details, see the published fee, pay with PayPal, and download a verifiable campus permit.

- Choose from a research catalog of 10 California colleges. AG Grid Community provides searchable, sortable campus-fee comparisons.
- Enter a vehicle in one sentence. The configured AI reader drafts the plate, make, model, color, and state; the person reviews and edits every field before continuing. A clearly labeled rules fallback keeps the demo usable when the model is unavailable.
- Students and public visitors can choose day or term permits. The employee demo illustrates an exemption. These are fictional personas and demonstration policies, not actual campus authorization.
- Review the exact server-calculated USD total before checkout. Prices come from the checked-in CSV catalog; the model cannot invent or change a fee. Missing prices block payment.
- Pay through PayPal Sandbox. Solvia issues a paid permit only after a completed capture matches its saved order, amount, and currency. The receipt and printable PDF include the PayPal order ID and capture ID, and a verification link checks the permit's saved status.

## How we built it

Solvia uses ASP.NET Core MVC on .NET 10, Entity Framework Core, and SQLite. Shared application and pricing services support both the conversational and conventional form paths. PayPal Orders REST APIs create and capture payments; the JavaScript SDK supplies checkout buttons. Repeated create/capture requests reuse saved references, and recovery checks provider state before declaring a permit paid.

The default AI adapter calls a configured Ollama endpoint with Microsoft Phi-4-mini. We also implemented an optional server-side Meta Model API adapter using Standard Muse Spark 1.3, with a private key, bounded requests and the same validated draft contract. Its live hosted verification remains pending. Vehicle extraction is a suggestion, with human review before persistence. The browser sends its form to the Solvia app; where AI processing runs depends on deployment. The Meta demo uses fictional adult personas and vehicle data; a real campus deployment would need an approved provider, age eligibility and data-handling policy.

AG Grid Community does real work comparing campus fees, with a native table fallback when its CDN is unavailable. PDFsharp generates printable permits; dependency and font licenses are documented in the repository. CSV pricing is intentionally sufficient for this hackathon prototype; no live college pricing feed is claimed.

## Challenges we ran into

Numeric license plates and labels such as "license plate number" initially failed extraction. We fixed those cases and added regressions. We also repaired broken service registration, navigation, fee-grid rendering, payment-error recovery, and payment references in receipts and PDFs. A successful return page alone must never be mistaken for captured money.

## Accomplishments we're proud of

On October 19, 2026, a real PayPal Sandbox test-card transaction for a $30 Fresno City College term permit completed through Solvia's REST capture endpoint. An independent PayPal order lookup confirmed the completed capture and exact USD amount. The actual paid receipt and PDF contain matching order and capture IDs. This verifies the test-card API path; a separate Personal Sandbox wallet approval demonstration remains to be recorded.

The application passed a full 367-test backend run with zero failures or skipped tests. We verified 82 distinct TypeScript Playwright checks across desktop and mobile, including 30 smoke checks, across retained full and targeted runs after harness corrections. A separate opt-in TypeScript integration matrix passed all 20 checks across four colleges: student/public day and term permits, employee exemptions, and blocked unpublished prices. Fourteen distinct PayPal captures completed for $945 in Sandbox test funds. Create and capture retries reused their original references, and independent provider lookups found one capture per order. All 16 student/public PDFs passed content validation. Two further real-provider checks verified unapproved-order and refused-card handling, while two actual SDK/no-JavaScript checks verified the official payment options and protected continuation without creating orders. Our quality-control working document records the process, failures, corrections and remaining release gates.

## What we learned

A simpler front end requires careful boundaries behind it: AI drafts the car details, the catalog supplies the price, the person confirms the information, and PayPal supplies payment truth. Showing those boundaries makes the experience easier to trust and debug.

## What's next for Solvia

Complete live hosted Meta verification through the actual app, measure first-time mobile usability, and record a public YouTube demo under three minutes. Direct local Phi-4-mini extraction worked but exceeded the app's response budget; labeled fallback is resilience, not proof of a live AI application path. Production use would require real authentication, campus-approved eligibility and pricing, and operational/privacy review.

## Hackathon progress and current status

Solvia is an independent application first built during this hackathon. Its development includes PayPal payment verification and recovery, vehicle extraction and review, printable permits, fee comparison, responsive styling, automated browser tests, and documentation. The repository records iterations made within the event. This entry is a working Sandbox prototype using fictional people, not a live campus payment service.

Source and setup: https://github.com/jlhpacheco/solvia
