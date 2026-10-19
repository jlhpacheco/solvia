# Solvia Sandbox seller branding

The existing PayPal Sandbox Test Store is the seller configured in Solvia.
Its business-profile merchant ID was compared with the payee merchant ID
on the app's real $30 USD order and matched on October 19, 2026.

## Saved profile settings

- Logo: the circular Solvia S/arrow mark, navy `#071b46`, white, and teal
  `#20b9a7`. The square PNG has transparent corners and fits PayPal's circle.
- Public business name: **Solvia**.
- Short and long credit-card statement names: **SOLVIA**.

PayPal's generated legal business name still appears as Test Store in the
merchant dashboard. The public name and payment descriptors identify Solvia.
The logo and public-name settings were saved in the actual Sandbox profile;
they are not a screenshot mockup or a change to PayPal's page source.

## Reusable assets

The app header uses `src/Solvia/wwwroot/img/solvia-avatar.svg`. A matching
512 by 512 PNG is stored alongside it for PayPal's upload control. Re-export
the PNG with the installed development browser dependency:

```powershell
node scripts/Export-SolviaAvatar.cjs
```

The mark extends the existing app's S/arrow design. A wide wordmark should
not be uploaded to PayPal's circular profile slot: PayPal crops its center,
which cuts off the name. Use the square avatar there.

## Payment testing

Opening the order while signed into the seller produced PayPal's explicit
cannot-pay-self response. This is expected: the seller cannot also be the
buyer for the same transaction. Use a separate Personal Sandbox buyer or
an isolated guest-card session with an official Sandbox test card. The
merchant's signed-in session is useful for checking received activity.

Branding and seller identity checks do not prove a completed transaction.
Verify PayPal's capture status, exact USD amount, saved capture/order IDs,
receipt, and PDF before reporting a successful payment. See
[Sandbox troubleshooting](PAYPAL-TROUBLESHOOTING.md) and
[PayPal's card-testing guide](https://developer.paypal.com/sandbox-testing/card-testing).
