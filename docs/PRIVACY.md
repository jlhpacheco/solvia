# Solvia demo data and privacy

Updated October 20, 2026. This describes the implemented architecture. It does not certify institutional, legal, or regulatory compliance.

## What the demo stores

The seeded people use fictional `.example` addresses and represent adults
(18+) in this demo. Vehicles, applications, prices, payment identifiers,
issuance details and webhook records are stored in SQLite on the application
host. Use fictional inputs. The person switcher/roster are demo tools, not
authentication or production authorization.

## AI data path

The browser sends the vehicle sentence to Solvia, then the server calls the
selected model provider:

- `AI_PROVIDER=together` is the default. It sends the vehicle sentence and
  extraction instructions/catalog context to Together AI's hosted HTTPS
  API using `Qwen/Qwen3.5-9B`. No payment credentials, payment tools or buyer
  account access are supplied to the model.
- Optional `AI_PROVIDER=ollama` runs Phi-4-mini at
  `http://localhost:11434` on the application host by default. A remote
  `OLLAMA_BASE_URL` sends the sentence to that configured machine.

The app performs inference, not training. Together's
[privacy documentation](https://docs.together.ai/docs/privacy-and-security)
states that prompts/responses are stored by default for product improvement;
training sharing is a separate opt-in, off by default. We have not verified
this account's settings and do not claim a no-training configuration.

Together [ZDR](https://docs.together.ai/docs/zero-data-retention) requires
disabling prompt/response storage in the settings governing the API key.
Organization settings govern organization-key traffic. ZDR is not enabled
by default, is not retroactive, and still retains operational metadata.
No ZDR/account-setting verification or US-only residency is claimed for
Solvia; serverless endpoints offer no region selection. These provider
policies do not establish institutional approval. Ollama operator practices
also depend on deployment.

Both adapters use the shared schema and plate safeguards. The response is
labeled `together`, `ollama`, or `fallback`; a failed/unusable model answer uses
rules. Input is bounded before inference, and the person reviews editable
vehicle fields before saving. The provider budget defaults to 10 seconds
and clamps configured values to 1–90 seconds.

Both public extraction routes also share admission control before calling
the reader: 20 requests per connection IP per minute and 60 globally per
process, with two simultaneous requests per IP and four globally. A rejected
request receives HTTP 429/Retry-After and is not sent to the model; the form
keeps its entered answers. Limits reset on restart and do not follow a
person across devices, authenticate a person or enforce a billing budget.
Multiple hosted instances need shared/gateway limits and provider spending
controls. These safeguards do not establish provider privacy settings.

Together's key is configured privately in Development user-secrets. The first
real application suite passed eight distinct cases, including a final
release rerun after credential replacement, all labeled `together`. This
verifies runtime inference for that run, not provider retention, training or
residency settings; those account controls remain unverified.
Historically, a direct warmed CPU Phi-4-mini call took 32.26 seconds; the
app's 10-second budget fell back. Configuration and direct model responses
are distinct from verified model use through the application.

The extraction contract has no price field. Server-side catalog rules determine fees after extraction; the person reviews the total.

## Hosted-provider access and credentials

`TOGETHER_API_KEY` belongs only in the server secret environment or
Development user-secrets (`Together:ApiKey`). User-secrets are per-user local
configuration, not an encrypted vault. Never put real keys in chat, logs,
screenshots, browser code, `.env.example` or Git. The app redacts authorization
headers and avoids logging raw hosted-provider response/error bodies.
`scripts/Set-TogetherCredentials.ps1` uses a hidden prompt and sends the key
through stdin to the Development secret store, rather than command arguments.

This demonstration uses fictional adult testers/personas. The demo switcher
does not verify age. A real deployment, including younger students, requires
an appropriate access, provider-terms and privacy review.

## Payments and public information

The payment client enforces PayPal's HTTPS sandbox API and USD. PayPal handles buyer credentials and approval. App credentials remain in environment variables or .NET user-secrets, outside Git.

Public verification shows permit standing and minimal permit facts, not the holder's name, email, or plate. Treat a verification link as shareable proof. The optional outbound webhook omits name, email, and plate; its permit number and verification URL are still operational identifiers. Configure only an intended recipient.

Payment confirmation does not by itself establish current parking validity.
Receipt headings use the shared date/status rule; paid denied, future and
expired records keep their payment facts without an active-permit claim.
The final 390-case backend run covers those receipt states and the shared
admission guard. Browser evidence is 84 desktop/phone checks plus a separate
16-case Chromium/WebKit tablet rerun; see [versioned testing](TESTING.md).
These functional results do not establish a security certification or
provider privacy setting.
Cancel/unknown-return messages direct people to inspect saved status rather
than promise that no charge occurred.

## Before a real campus deployment

Replace the demo switcher with authentication and role-based access. Review the roster, retention, backups, model endpoint, telemetry, payment setup, and campus policies with the institution. This prototype is not connected to a campus authorization system. Printed demo permits do not grant parking rights.

Background for institutional review: [California Community Colleges AI guidance](https://ai.cccco.edu/guidance-and-policy/) and [California Department of Education AI policy](https://www.cde.ca.gov/ci/pl/aipolicy.asp). These references do not establish app approval.
