# Solvia demo data and privacy

Updated October 19, 2026. This describes the implemented architecture. It does not certify institutional, legal, or regulatory compliance.

## What the demo stores

The seeded people use fictional `.example` addresses. Vehicles, applications, prices, payment identifiers, issuance details, and webhook records are stored in SQLite on the **application host**. Use fictional inputs. The public person switcher and college roster are demo tools, not authentication or a production authorization system.

## AI data path

The browser sends answers to Solvia. The app calls the configured Ollama endpoint. Its default is `http://localhost:11434`, local to the **server running Solvia**. For a local install that is the developer's computer. For a hosted install it is the host, not the student's browser. Changing `OLLAMA_BASE_URL` can send answers to another machine.

Microsoft Phi-4-mini is the selected model. The app performs inference and does not implement training. That is not a guarantee about every possible endpoint operator's retention or training policies. Review deployment and provider configuration before using real information. The deterministic fallback is labeled when the model cannot respond.

The extraction contract has no price field. Server-side catalog rules determine fees after extraction; the person reviews the total.

## Payments and public information

The payment client enforces PayPal's HTTPS sandbox API and USD. PayPal handles buyer credentials and approval. App credentials remain in environment variables or .NET user-secrets, outside Git.

Public verification shows permit standing and minimal permit facts, not the holder's name, email, or plate. Treat a verification link as shareable proof. The optional outbound webhook omits name, email, and plate; its permit number and verification URL are still operational identifiers. Configure only an intended recipient.

## Before a real campus deployment

Replace the demo switcher with authentication and role-based access. Review the roster, retention, backups, model endpoint, telemetry, payment setup, and campus policies with the institution. This prototype is not connected to a campus authorization system. Printed demo permits do not grant parking rights.

Background for institutional review: [California Community Colleges AI guidance](https://ai.cccco.edu/guidance-and-policy/) and [California Department of Education AI policy](https://www.cde.ca.gov/ci/pl/aipolicy.asp). These references do not establish app approval.
