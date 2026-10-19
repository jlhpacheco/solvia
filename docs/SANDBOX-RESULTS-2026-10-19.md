# Solvia multi-college Sandbox verification - October 19, 2026

**The original 20-case matrix and two supplementary negative provider checks passed: 22 Sandbox integration checks in total.** Two additional actual remote SDK/JavaScript-disabled smoke checks also passed and are recorded separately. The runs used the real Solvia preview at localhost:5156; payment tests called the actual PayPal Sandbox REST API with official test-card funds. No real money was charged.

One successful pilot was preserved, then the remaining 19 cases ran without rebuying that pilot. The two Playwright reports contain 20 expected passes, zero failures, zero skips, and zero flaky results.

| Validation group | Verified passes | Payment effect |
|---|---:|---|
| Original multi-college matrix | 20 | 14 completed captures, $945.00 USD gross Sandbox test funds |
| Supplementary negative provider cases | 2 | No completed payment and no paid permit issued |
| Sandbox integration total | 22 | Successful capture count and total remain 14 / $945.00 USD |
| Actual remote SDK and JavaScript-disabled smoke | 2, separate | No PayPal order creation or capture |

## Coverage and results

| College | Term USD | Day USD | Student term/day | Public term/day |
|---|---:|---:|---|---|
| CSU Monterey Bay | 229.50 | 5.00 | Passed / Passed | Passed / Passed |
| CSU East Bay | 195.00 | 10.00 | Passed / Passed | Passed / Passed |
| Fresno City College | 30.00 | 1.00 | Passed / Passed | Passed / Passed |
| Modesto Junior College / Columbia College / YCCD | 0.00 | 2.00 | Passed / Passed | Passed / Passed |

The 16 student/public cases produced **14 completed payments totaling $945.00 USD gross test funds** and two free term permits. Two additional employee day/term cases issued exempt permits without payment. Two unpublished CSU Bakersfield Summer-price cases stayed blocked for Student and Public.

Student Maria, Public Pat, and Employee Elena are fictional demo personas. Public uses the demo catalog pricing; the YCCD Public free-term result and employee exemption are demonstration policies, not assertions about real campus eligibility. CSV pricing remains the accepted hackathon scope.

## What was verified

- The selected college, vehicle plate, category, permit type, and exact fee survived review and checkout.
- Solvia created the order through its antiforgery-protected endpoint; repeating creation returned the same order.
- An independent PayPal lookup matched the configured Sandbox seller, exact amount, and USD currency before approval.
- Official Sandbox card confirmation approved the order; Solvia's actual REST endpoint captured it and issued a saved receipt.
- Repeating capture returned the same capture and receipt references. Each provider order contained exactly one completed capture.
- Each paid receipt showed the matching order and capture IDs. Each matrix case downloaded an actual PDF.
- Public verification succeeded without the buyer cookie and did not reveal the full vehicle plate. The initial pilot checked verification using its buyer session; the remaining cases additionally checked anonymous access.

PDF content and Letter-page validation are recorded separately in the local PDF validation report. The saved raw evidence, PDFs, and reports are ignored by Git; the reusable TypeScript suite and this sanitized summary are committed.

## Completed payment references

| College | Buyer category | Permit | USD | PayPal order | PayPal capture | Receipt |
|---|---|---|---:|---|---|---|
| CSU Monterey Bay | Public | day | 5.00 | 9CS56658T3671105C | 44C16660LY821100C | SOL-2026-825674 |
| CSU Monterey Bay | Public | term | 229.50 | 1YK487095E852152N | 1T3976445X5709506 | SOL-2026-119570 |
| CSU Monterey Bay | Student | day | 5.00 | 8CE0321685347650D | 7CP436742A4995146 | SOL-2026-002664 |
| CSU Monterey Bay | Student | term | 229.50 | 41782737AM8664933 | 00S51821NW347174Y | SOL-2026-368714 |
| CSU East Bay | Public | day | 10.00 | 7EN8197001862462Y | 2A570212DE089102J | SOL-2026-590271 |
| CSU East Bay | Public | term | 195.00 | 6G717031GV1826746 | 9NY8982211018661S | SOL-2026-246383 |
| CSU East Bay | Student | day | 10.00 | 2T7488033R9326441 | 28D08854DX6702616 | SOL-2026-417973 |
| CSU East Bay | Student | term | 195.00 | 18Y04094X06155601 | 87H75810FY937291D | SOL-2026-362055 |
| Fresno City College | Public | day | 1.00 | 77T76342TC391071M | 3T0492636L2372140 | SOL-2026-108559 |
| Fresno City College | Public | term | 30.00 | 2RW58803081662504 | 7R115254H5021702W | SOL-2026-502538 |
| Fresno City College | Student | day | 1.00 | 1HB7563932528350M | 9AT98589C9212023K | SOL-2026-247042 |
| Fresno City College | Student | term | 30.00 | 40C04098PG636204X | 8JC36217HE061341S | SOL-2026-883195 |
| Modesto Junior College / Columbia College / YCCD | Public | day | 2.00 | 1GM57482ME293150P | 4WA10712YS489992X | SOL-2026-613775 |
| Modesto Junior College / Columbia College / YCCD | Student | day | 2.00 | 0D311799177980216 | 6CC87799PA7378948 | SOL-2026-098158 |

## Supplementary real-provider negative checks

Both additional cases used unique fictional Maria/Fresno City College day
applications at $1.00 USD. They tested actual provider responses and the
running app's receipt and PDF gates:

| Case | Provider and app result | Issuance result |
|---|---|---|
| Unapproved order | Order `66J41507BL238010X` stayed `CREATED` with zero captures. Solvia returned HTTP 409 and `ORDER_NOT_APPROVED` | Receipt remained **Not paid yet**; no live verification link; owned PDF request returned HTTP 400 |
| Official refused-card trigger | Order `3YW31142HP525983D`; capture `5K2698981U4039151` was `DECLINED` with processor code `0500`. Solvia returned HTTP 200, `completed: false`, `status: DECLINED` | No successful receipt or paid permit minted; no live verification link; owned PDF request returned HTTP 400 |

The two checks passed across targeted runs. An earlier refused-case run found
an overly strict test assertion: PayPal added an optional amount breakdown
while retaining the exact $1.00/USD total. The assertion was corrected to
verify value/currency, then only the refused case was rerun successfully.
This was a test correction, not an application payment fix. These cases add
no successful captures or funds to the original 14 / $945.00 matrix totals.
Sanitized records are stored locally in
`sandbox-results/negative-evidence/`. The refusal trigger is documented in
[PayPal's card-testing guide](https://developer.paypal.com/sandbox-testing/card-testing/).

## Actual native SDK and no-JavaScript smoke

The separate opt-in `tests/sandbox/native-options.spec.ts` run passed **two
checks in 33.5 seconds** against localhost:5156. It loaded PayPal's actual
remote SDK and observed the **PayPal** and **Debit or Credit Card** options,
with the duplicate redirect CTA hidden. A second browser context with
JavaScript disabled showed one **Continue to PayPal** form with an
antiforgery token and the correct application-specific action.

The smoke created a fictional unpaid application but **no PayPal order**;
it did not click a payment option, confirm a source or capture funds.
Real SDK rendering is now verified. Personal-buyer login and hosted wallet
approval/capture remain unverified.

## Verification boundaries

This is provider-confirmed Sandbox test-card/API evidence. It does not claim that a separate Personal buyer completed PayPal's hosted wallet UI, or that live payments, refunds, or real webhook delivery were verified. All four campuses route to the same Solvia Sandbox seller in this demonstration; no real college settlement accounts are connected.

The official PayPal SDK supplies branded checkout buttons. A button creates a server-priced order for the selected campus/permit; separate fixed-price dashboard buttons are unnecessary. See [the opt-in test instructions](SANDBOX-MATRIX.md) for reproducing the checks.

Live Phi-4-mini inference, first-time user feedback, and the final public demo video remain separate hackathon readiness work.
