# PayPal AI Hackathon readiness

Reviewed October 19, 2026 against the [official rules](https://paypalaihackathon.devpost.com/rules)
and [event overview](https://paypalaihackathon.devpost.com/). This is a project
audit, not an organizer decision or confirmation of submission.

## Official requirements to finish

The submission deadline is **November 12, 2026 at noon Pacific**. Judging is
December 1-15; results are expected around December 21. Confirm participant
eligibility, team details, registration, and applicable geographic exclusions.

Submit a functional application meaningfully combining **PayPal Sandbox and
AI**, English instructions, a public source repository with an open-source
license, and a public YouTube demonstration shorter than three minutes.
Provide free judge access and identify the tools/models used. Existing work
is allowed when substantially updated during the event; disclose the starting
point and changes. Do not claim the earlier CarPermitApp as new work.

Sponsor prize entries require actual use of that sponsor's tool. Solvia's
working fee comparison uses AG Grid Community. Planned Elastic, Render,
Zapier, APIMatic, or KERNEL work is not evidence of completed integration.

Check the linked rules before submitting, including IP permissions and
participant obligations. Registration and submission have not been verified.

## How the app will be judged

After the initial viability check, the five criteria have equal weight (20%
each). The table is our assessment, not an official numeric score.

| Criterion | Evidence in Solvia | What would improve the entry |
|---|---|---|
| Technological implementation | Shared application/pricing services; verified capture amounts and USD; webhook retries; automated route and domain tests | Record real sandbox buyer approval/capture and a real model response; test from a clean judge machine |
| Design | Solvia navy/teal layout, clear total, person switcher, responsive workspace, fee search | Observe a first-time user completing a permit on a phone |
| Potential impact | Published fees, one application path, receipts and printable permits | Interview students/employees; measure completion time and errors; avoid invented savings |
| Innovation/idea | Plain-language permit drafting with deterministic pricing and payment verification | Show the benefit over an ordinary form and explain safeguards precisely |
| Presentation | Working demo, documented setup, open-source notices | Record a focused story and an uninterrupted actual transaction; finish English Devpost text |

Technical strength is improving. Live integration evidence and presentation
remain the most important gaps. A fallback parser is useful resilience but
does not demonstrate meaningful AI on its own.

## Participant checklist

- [ ] Confirm eligibility, team membership, registration, and acceptance of the rules.
- [ ] Rotate the sandbox secret pasted in chat; set it privately with `scripts/Set-SandboxCredentials.ps1`.
- [ ] Use a separate sandbox Personal buyer to approve and capture a paid permit. Verify the receipt and PDF.
- [ ] Run Ollama/Phi-4-mini and confirm an extraction explicitly labeled `ollama`.
- [ ] If demonstrating webhook recovery, register an HTTPS sandbox listener, configure its webhook ID privately, and test a real verified event. No subscription was present when the dashboard was inspected.
- [ ] Confirm the employee/free permit flow and print a Letter-size PDF.
- [ ] Show AG Grid sorting and campus search in the actual app.
- [ ] Prepare judge access for the full judging period; local instructions ship, hosting is not deployed.
- [ ] Record the public video, complete English submission, and disclose prior work.
- [ ] Review and submit explicitly on Devpost. No submission has been made by this review.
