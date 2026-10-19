# Solvia: lessons from winners in events Jose entered

Research date: October 19, 2026. This is a submission strategy, not an organizer score or a prediction of an award.

Solvia's strongest story is one complete, trustworthy permit purchase: describe a car, review the extracted details, see the published campus price, pay through PayPal Sandbox, and receive a permit with verifiable payment references. The original $30 capture and the subsequent 20-check matrix now support that story: 14 distinct captures totaled $945 in test funds across four colleges, with retry safety and 16 validated permit PDFs. The next decisive proof is a real AI response and a short recording of the complete experience.

## Scope and evidence

This review focuses on events Jose entered. The signed-in [Devpost hackathon history](https://devpost.com/jlpacheco/challenges) was checked in the browser: CALL-E and YouCam show submitted entries, and Square + Google AI appears in his history. His public [LastCall Materials project](https://devpost.com/software/lastcall-materials) independently identifies CALL-E under “Submitted to.”

The [CALL-E gallery](https://call-e.devpost.com/project-gallery), [YouCam gallery](https://youcam-api.devpost.com/project-gallery), and [Square + Google AI gallery](https://square-google-ai.devpost.com/project-gallery) identify the selected winners below. CALL-E and YouCam are recent 2026 examples. Square + Google AI is an older **2023** comparison included because its AI-plus-commerce design is particularly relevant. This is a focused sample, not an exhaustive review of every entered event.

The [CALL-E announcement](https://call-e.devpost.com/updates/45445-and-the-winner-is), [YouCam updates](https://youcam-api.devpost.com/updates), and [Square updates](https://square-google-ai.devpost.com/updates) direct readers to the winners. These sources do not publish individual scorecards or establish the precise cause of each award. Unrelated events are excluded.

## What the recent winners actually show

The award labels are verified. Implementation descriptions below are the entrants' descriptions; their claimed user outcomes and test counts have not been independently audited. The Solvia lessons are our interpretation, not statements from the judges.

| Winner and verified award | Evidence on the project page | Application to Solvia — our inference |
|---|---|---|
| [Local atlas with 1st party FAQs](https://devpost.com/software/local-atlas-with-1st-party-faqs) — Most Practical Use Case | A local question becomes a dated, quoted answer that can be reused. Fresh stored answers prevent repeat calls. The product includes an explicit confirmation, uncertainty, and a credential-free review mode. | Keep the published fee and source visible. An AI draft should reduce repeated typing while the actual price and payment state stay authoritative. Give judges a dependable way to inspect the whole workflow. |
| [Conversation-Clarify](https://devpost.com/software/conversation-clarify) — Most Innovative Use Case | An ambiguous Gmail reply leads to one approved clarification call and a human-reviewed draft. Its page supplies a concrete example, describes a real call, and distinguishes offline fixtures from live execution. | Show the exact sentence that confused an ordinary parser and the real model's useful extraction. Then show the user correcting or confirming it. Explain the one place AI helps. |
| [Care Call AI](https://devpost.com/software/care-call-ai) — Honorable Mention | The described workflow continues from reviewed outreach through structured needs and printable handoff orders. It includes a coordinator approval gate and a working judge-access path. | A PayPal button is one step. The valuable product finishes with a receipt, printable permit, and clear verification. Make those outputs part of the main demo. |
| [RingBack](https://devpost.com/software/ringback-sumnr6) — Honorable Mention | A canceled appointment triggers a sequential calling campaign that stops after acceptance and updates the appointment book. Its page distinguishes provider failure, no answer, and an unknown result, with recovery that does not redial. | Demonstrate payment recovery: a timeout must not become “paid,” and retrying must not produce a second charge. Keep an unpaid order, completed capture, and issued permit distinct. |

The [CALL-E criteria](https://call-e.devpost.com/) emphasize a specific real problem, meaningful runtime integration, a clear idea, and a coherent product/demo. All four winners present a recognizable task with an observable end result. That is a useful pattern; it does not establish that any single feature caused a win.

### YouCam: a specific audience and inspectable result

| Winner and verified award | Evidence on the project page | Application to Solvia — our inference |
|---|---|---|
| [Aloud](https://devpost.com/software/aloud-lxqr70) — 1st Place | A beauty-shopping experience designed for blind users, including sound-guided capture and spoken answers grounded in structured API results. Its write-up describes a real-device capture experiment and offers working judge links. | Define the person and task sharply. Show that an unfamiliar user can complete the permit flow, with accessible labels, clear feedback, and useful correction. Test a concrete hypothesis instead of assuming convenience. |
| [ShadeSpan](https://devpost.com/software/shadespan) — 2nd Place | Applies virtual try-on to a merchant's catalog audit rather than a standard shopper preview. It produces comparisons and a report; the creator describes measured render-fidelity limitations and keeps warnings separate from scores. | Differentiate through a useful workflow and verifiable output. Make the comparison between a long permit form and Solvia's reviewed draft visible. Preserve “unknown” instead of inventing a fee or vehicle field. |
| [CASTING](https://devpost.com/software/casting) — 3rd–5th Place | A product photo becomes an eight-person coverage board and export. The write-up identifies panel gaps, partial results, and labeled precomputed runs rather than presenting them as fresh live results. | Distinguish live AI, fallback parsing, successful payment, and pending payment in both the UI and video. A partial or simulated result must say what actually happened. |

The [YouCam event criteria](https://youcam-api.devpost.com/) ask for real consumer/retail value, meaningful integration, a complete product, and an audience-specific impact case. The inference for Solvia is to demonstrate one person reaching the permit outcome and explain exactly what improved for that person. Winner pages alone do not validate their reported commercial impact.

### Square + Google AI: AI and commerce in the same job

| Winner and verified award | Evidence on the project page | Application to Solvia — our inference |
|---|---|---|
| [SquareSense AI](https://devpost.com/software/squaresense-ai-personalized-ecommerce-intelligence) — First Place Overall | Uses store data for conversational analysis and interactive charts. The project provides explicit Sandbox and test-mode access instructions for judges. | Connect AI to the selected permit/vehicle context and make judge access easy. Keep credentials out of the public repository while providing complete setup and test instructions. |
| [Aisle - Visionary POS](https://devpost.com/software/falcon-pos-add-vision-and-speed-to-your-checkout) — Second Place Overall | Camera recognition adds products to an order that is sent to Square Terminal. The page discusses a barcode-checkout comparison and distinguishes its provider testing limitation. | Show a direct chain from useful AI assistance to a payment outcome. Measure Solvia against the ordinary entry task, and distinguish verified REST capture from an untested wallet UI path. |

The 2023 event's historical API limitations are not current guidance for Solvia. The transferable point is the visible connection between a user's task, AI assistance, commerce data, and a completed transaction.

## What PayPal judges are explicitly assessing

The [PayPal official rules, section 6](https://paypalaihackathon.devpost.com/rules), first apply a viability/theme gate. Projects passing it face five equally weighted criteria, so each accounts for **20%**: technological implementation, design, potential impact, innovation/idea, and presentation. The rules prioritize substantive PayPal-plus-AI use, a coherent product, a credible audience/problem, differentiation, and a clear working demonstration. Implementation is the first tiebreak criterion.

The same rules require a functional build judges can access, a public repository with an open-source license, and a public YouTube demonstration under three minutes. Complete setup instructions can provide judge access; a hosted site is not mandatory. Existing projects must explain significant work completed during the event. The deadline is **November 12, 2026 at noon Pacific**.

| Criterion | Solvia evidence today | Next proof to make the case stronger |
|---|---|---|
| Technological implementation | The actual PayPal Sandbox provider completed the original $30 payment plus a 20-check matrix covering four colleges and Student/Public day/term choices. Fourteen matrix captures totaled $945 in test funds, and retry checks found one capture per order. | Demonstrate the configured AI model running and record an interactive buyer checkout. Keep provider evidence separate from simulated browser callbacks. |
| Design | A consistent Solvia identity, responsive flow, editable vehicle review, clear checkout total, and printable output. | Observe someone unfamiliar with the app completing the task on a phone. Fix confusion they encounter and show the result. |
| Potential impact | Named campus audiences, published fee data, student/public day and term choices, and a demo employee path. | Ask a small set of students, visitors, and campus staff to try a defined task. Record actual completion time, errors, and comments. Do not claim reduced workload or savings before measuring them. |
| Innovation/idea | The combination of plain-language entry, human confirmation, deterministic prices, verified payment, and portable permit proof. | Explain the advantage over a normal permit form in one example. A chatbot alone is weak differentiation. |
| Presentation | A working core flow and actual Sandbox payment evidence now exist. | Record one easy-to-follow journey that reaches a paid receipt and permit. Show the AI and PayPal steps functioning, with Sandbox clearly labeled. |

These are readiness observations, not fabricated judge scores. The verified capture used official Sandbox test-card confirmation followed by Solvia's REST endpoint. It does **not** establish that a Personal-wallet buyer completed the hosted checkout UI. A fallback parser also does **not** prove a working AI-model demonstration.

## Assessment of the app now

Solvia has progressed from a permit prototype into a credible hackathon product: the user can move toward a real provider-confirmed Sandbox payment and receive the paid document. Consistent branding, fee comparison, numeric-plate corrections, browser coverage, and payment recovery support that central experience.

The remaining competitive weakness is the evidence surrounding it. Live AI inference still needs verification; there is no measured user study; and the final public video is missing. Demo personas are convenient for judging but are not production identity verification. The CSV catalog is the accepted hackathon scope and should be described honestly as curated published data, with no live-sync claim. Those boundaries help judges understand exactly what they can test.

## Recommended changes to the submission

Lead with this sentence:

> Solvia turns a plain-language car description into a campus permit with a published price and verifiable PayPal payment.

Use one fictional person and one paid campus permit for the main narrative. Briefly show that students and public visitors can choose a day or term permit, then show a second campus to demonstrate pricing is tied to the selected college. Keep a free or exempt permit separate from payment evidence. AG Grid should appear as the working fee comparison used to make a decision, rather than as a sponsor badge.

The write-up should state what the AI drafts, what the user confirms, what the server prices, and what PayPal verifies. Describe Solvia as an independent new project built during this hackathon, as the participant confirmed. Remove planned tools from “Built with,” absolute privacy guarantees, and unsupported compliance or impact claims. Preserve a clear “working today / next” boundary.

Priorities before submission:

1. Verify the real model path with representative vehicle sentences and preserve editable review. Explain fallback mode when the model is unavailable.
2. Preserve the completed multi-college Sandbox matrix as payment evidence and record a clear interactive checkout. The fee, capture, receipt, PDF, and duplicate-payment checks have passed; see [dated results](SANDBOX-RESULTS-2026-10-19.md).
3. Test a clean judge setup and record the main journey. Keep setup and test instructions short and reproducible.
4. Collect modest real user feedback and report only what was observed.
5. Finish the Devpost draft and public video. Review every claim against the actual build before final submission.

## Suggested 175-second demonstration

| Time | What judges see |
|---|---|
| 0–15 seconds | A student or visitor needs a permit and wants a clear price and proof of payment. State that all shown people and payments are Sandbox examples. |
| 15–50 seconds | Choose a college, describe a car, run the actual AI path, review the plate/make/model/color, and correct a field if necessary. |
| 50–75 seconds | Show day/term choice, the published fee/source, and the useful AG Grid comparison. |
| 75–125 seconds | Complete an actual Sandbox checkout, then show completed capture status in Solvia. Record the buyer method that truly works rather than substituting mocked success. |
| 125–150 seconds | Open the receipt and printable permit, including PayPal order/capture references and permit verification. |
| 150–165 seconds | Briefly show the public-person option and a second college with a different fee. |
| 165–175 seconds | Explain the boundary in one sentence: AI drafts, the human confirms, server code determines the fee, and PayPal confirms payment. Show the repository/setup link. |

The payment identifiers are supporting evidence. The memorable result is that a person leaves with the correct permit and a clear, trustworthy record.
