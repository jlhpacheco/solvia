# Solvia visual direction

The brand remains **Solvia**. The supplied references informed the navy/teal
palette, whitespace, rounded cards, clear hierarchy, and institutional
workspace. They are visual references, not instructions to claim their
audit, security, regulatory, or legal capabilities.

## Components

| Token/component | Value and purpose |
|---|---|
| Navy | `#071b46`: headings, navigation rail, PDF header |
| Teal | `#007f79`: emphasis, active navigation, permit/payment accents |
| Blue | `#1357db`: primary action |
| Surfaces | White cards on pale gray; restrained borders and shadows |
| Navigation | White header, desktop workspace rail, compact mobile links |
| Forms | Visible labels, validation summaries, focus outlines, CSRF-protected writes |
| Tables | AG Grid Quartz theme, campus search, price sorting, wrapped source notes |
| Honesty | Sandbox badge, fictional-person disclosure, labeled AI fallback, no fabricated dashboard statistics |
| Print | One-page US Letter permit with large plate, dates, standing, and demo footer |

Primary journey: **choose campus -> describe vehicle -> review total -> approve
PayPal -> view receipt/permit**. The classic form remains available. Checkout
links stay in the current tab.

## Competitor observations

The public [MyCampusPermit directory](https://mycampuspermit.com/) leads to
campus-specific pages. The [Fresno page](https://mycampuspermit.com/fresno-city.html)
puts purchase options and policy/price information across separate sections.
The [CSU Monterey Bay page](https://mycampuspermit.com/mbay.html) has detailed
affiliation rates and campus rules. These observations concern public pages;
authenticated checkout was not reviewed. There is no measured abandonment
or speed comparison.

Solvia's opportunity is to make the next action and exact applicable amount
clear in one journey. Campus affiliation rules must still be respected:
employee exemption is a demo policy, not a rule for every college. Catalog
provenance remains visible and missing prices are never guessed.

## Sponsor integration

The fee page uses AG Grid Community 34.3.1 at runtime. It now uses the
[JavaScript Theming API](https://www.ag-grid.com/javascript-data-grid/theming-migration/)
without conflicting legacy CSS. Its search uses the documented
[Quick Filter API](https://www.ag-grid.com/javascript-data-grid/filter-quick/).
A native table/search fallback remains available if the CDN is unreachable.
