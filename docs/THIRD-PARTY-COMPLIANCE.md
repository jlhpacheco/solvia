# Third-party authorization: evidence and remaining checks

Reviewed October 19, 2026 against this repository and the primary sources
linked below. This supports the hackathon's third-party-rights review; it
does not certify legal clearance or answer the entrant's attestations.
The participant remains responsible for rights, account authority and
submission accuracy. No eligibility checkbox or agreement was accepted
through this review.

The [component inventory](../NOTICES.md) and [Solvia MIT license](../LICENSE)
serve different purposes. Solvia's license covers its own licensed work;
upstream code, fonts, services, brands and data retain their own conditions.
An applicable open-source grant can supply permission subject to its
conditions. Paying for API access does not establish every other right.

## License and integration evidence

| Item | Actual use and primary evidence | Conditions / remaining check |
|---|---|---|
| Together hosted API | The participant authorized use of their paid account and replacement key. `TogetherExtractionService` calls the fixed HTTPS endpoint with server-only credentials and `Qwen/Qwen3.5-9B`; no provider weights are bundled. [Together Terms](https://www.together.ai/terms-of-service), updated May 19, 2026, govern the service separately from the model. | Account authority/billing remain the participant's responsibility. Section 2.4 requires observing model terms. Payment alone is not full authorization evidence. |
| Qwen3.5-9B | The [official Qwen license](https://huggingface.co/Qwen/Qwen3.5-9B/blob/main/LICENSE) is Apache-2.0. Solvia consumes hosted inference, rather than redistributing or modifying weights. | If weights or derivative model files are later distributed, preserve the Apache license and applicable notices, identify modifications and review the distribution conditions. No general trademark grant is implied. |
| AG Grid Community 34.3.1 | `package.json` pins Community, and `_FeeTable.cshtml` loads that Community CDN bundle. The [publisher's Community license](https://www.ag-grid.com/eula/community/) is MIT; the installed package retains its copyright/license. | Preserve MIT notices in distributed copies. Community permission does not authorize Enterprise features; no Enterprise package, trial or key is used here. |
| PDFsharp 6.2.3 | `Solvia.csproj` pins 6.2.3. Both the [exact release license](https://github.com/empira/PDFsharp/blob/v6.2.3/LICENSE) and [publisher package listing](https://www.nuget.org/packages/PDFsharp/6.2.3) identify MIT; the restored package metadata agrees. | Preserve copyright/permission notices. Current upstream `master` now advertises AGPL/commercial licensing; it is not evidence for the pinned release. Re-audit any upgrade. |
| Embedded Roboto Regular/Bold | `Assets/Fonts/LICENSE.txt` retains Apache-2.0; `provenance.json` pins the Roboto-2 upstream commit. The [upstream Roboto-2 repository](https://github.com/googlefonts/roboto-2) identifies Apache-2.0. | These particular assets are Apache-licensed, not an assumed OFL release. Retain the license, provenance and applicable font notices when distributing the app/fonts. |
| Other dependencies | [NOTICES](../NOTICES.md) inventories framework, vendored web and test packages. Full vendored licenses exist under `wwwroot/lib/`; application and test package references identify their versions. | Keep upstream notices through publishing. This is not an exhaustive transitive-package or patent-clearance audit. |

## Hosted input and branding boundaries

[Together Terms](https://www.together.ai/terms-of-service) section 4.1 excludes
financial, medical and sensitive personal information. Use fictional vehicle
examples. The adapter does not automatically attach PayPal credentials or
payment records, but its text box does not filter every sensitive input.
Do not enter real card, bank, government-identity or medical details there.

Section 14.3 restricts company-name/logo/trademark use without prior written
consent. No separate promotional branding permission was established here.
Keep provider identification truthful; resolve public promotional/logo use
with the provider rather than treating API payment as endorsement permission.
Account storage/training/residency controls remain unverified; see
[the architecture and privacy limits](PRIVACY.md).

## PayPal testing permission and limits

The [PayPal Developer Agreement](https://www.paypal.com/us/legalhub/paypal/xdeveloper-full)
permits Sandbox API testing with anonymous, non-live data (section 2) and
licenses integration/marks subject to its requirements (section 9).
Solvia loads PayPal's official JavaScript SDK for branded options and uses
the Sandbox REST endpoint. Test cards and refusal triggers come from
[PayPal's card-testing documentation](https://developer.paypal.com/credit-card-number-generator).
The [dated results](SANDBOX-RESULTS-2026-10-19.md) document that testing,
separately from unverified live payments and wallet approval.

Preserve credential confidentiality, the separate buyer/seller test roles
and official SDK branding. A real launch additionally needs applicable
account/product approval, consent, support, refund and privacy arrangements;
this review does not establish those. No real student data or real cards
are needed for this demonstration.

## Fee data, campus names and media

`data/fees.csv` cites PMB campus pages per row. The
[Fresno City College source](https://mycampuspermit.com/fresno-city.html),
for example, publishes the researched numeric fees and retains its own
copyright notice. Attribution is provenance, not a grant to copy an entire
website, its wording, logos, artwork or user records.

The catalog is a dated demonstration fixture. Campus names identify research
sources; they do not establish affiliation, merchant settlement authority or
permission to issue real campus permits. Student/Public pricing and Employee
exemptions are demo rules, not campus policy certification. No campus/PMB
authorization letter was found in the reviewed repository.

Before public submission, the entrant should record the basis for reuse of
the fee compilation and any copied wording, and verify rights to the Solvia
logo, screenshots, reference artwork, music, narration and contributor work.
Retain source attribution; remove or obtain permission for any material
without a sufficient rights basis. The supplied reference images are not
evidence of rights clearance. A production campus offering requires separate
institutional agreements and data/privacy review.

## Unfinished participant decisions

- Confirm the account holder's authority, applicable service terms and any
  required third-party promotional permissions.
- Verify media/data/contributor rights and preserve license notices in the
  actual published artifact, not just the source checkout.
- Review the current hackathon rules before making the third-party-rights
  affirmation. This checklist does not make or authorize that affirmation.

This review changed documentation only. It made no purchase, provider-account
setting change, external permission request or legal attestation.
