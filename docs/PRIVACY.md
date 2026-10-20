# Solvia demo data and privacy

Updated October 19, 2026. This describes the implemented architecture. It does not certify institutional, legal, or regulatory compliance.

## What the demo stores

The seeded people use fictional `.example` addresses and represent adults
(18+) in this demo. Vehicles, applications, prices, payment identifiers,
issuance details and webhook records are stored in SQLite on the application
host. Use fictional inputs. The person switcher/roster are demo tools, not
authentication or production authorization.

## AI data path

The browser sends the vehicle sentence to Solvia, then the server calls the
explicitly selected model provider:

- `AI_PROVIDER=ollama` is the default. Phi-4-mini runs at
  `http://localhost:11434` on the application host by default. A remote
  `OLLAMA_BASE_URL` sends the sentence to that configured machine.
- `AI_PROVIDER=meta` sends the sentence and extraction instructions/catalog
  context to Meta's hosted HTTPS Model API. Its Standard model defaults to
  `muse-spark-1.3`; Contributor IDs are rejected. No payment credentials,
  payment tools or buyer account access are supplied to the model.

The app performs inference, not training. For Meta Standard, the
[current terms](https://dev.meta.ai/legal/terms-of-service), sections 4–5,
exclude Content from model training but allow retention/processing for
service, legal, security and policy purposes. We claim neither zero data
retention nor US-only residency. `store: false` does not create either
guarantee. Ollama operator practices also depend on deployment.

Both adapters use the shared schema and plate safeguards. The response is
labeled `ollama`, `meta`, or `fallback`; a failed/unusable model answer uses
rules. Input is bounded before inference, and the person reviews editable
vehicle fields before saving. The provider budget defaults to 10 seconds
and clamps configured values to 1–90 seconds.

At this checkpoint, no Meta key/live request has been verified. A direct
warmed CPU Phi-4-mini call took 32.26 seconds; the app's default budget fell
back. Configuration and direct model responses are distinct from verified
model use through the application.

The extraction contract has no price field. Server-side catalog rules determine fees after extraction; the person reviews the total.

## Hosted-provider access and credentials

`META_API_KEY` belongs only in the server secret environment or Development
user-secrets. `scripts/Set-MetaCredentials.ps1` uses a hidden prompt and stdin,
keeping the key out of command arguments. User-secrets are per-user local
configuration, not an encrypted vault. Never put real keys in chat, logs,
screenshots, browser code, `.env.example` or Git. The app redacts authorization
headers and avoids logging raw hosted-provider response/error bodies.

Meta's [terms, section 10.1](https://dev.meta.ai/legal/terms-of-service), require
users/end users to be 18+ and prohibit targeting integrated products at under-18s.
Meta mode is scoped to adult testers and fictional adult personas. The demo
switcher does not verify age. A real deployment including younger students
requires a different provider/design and an appropriate access/privacy review.

## Payments and public information

The payment client enforces PayPal's HTTPS sandbox API and USD. PayPal handles buyer credentials and approval. App credentials remain in environment variables or .NET user-secrets, outside Git.

Public verification shows permit standing and minimal permit facts, not the holder's name, email, or plate. Treat a verification link as shareable proof. The optional outbound webhook omits name, email, and plate; its permit number and verification URL are still operational identifiers. Configure only an intended recipient.

## Before a real campus deployment

Replace the demo switcher with authentication and role-based access. Review the roster, retention, backups, model endpoint, telemetry, payment setup, and campus policies with the institution. This prototype is not connected to a campus authorization system. Printed demo permits do not grant parking rights.

Background for institutional review: [California Community Colleges AI guidance](https://ai.cccco.edu/guidance-and-policy/) and [California Department of Education AI policy](https://www.cde.ca.gov/ci/pl/aipolicy.asp). These references do not establish app approval.
