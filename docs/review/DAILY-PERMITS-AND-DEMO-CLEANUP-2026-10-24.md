# Daily permits and local demo cleanup — October 24, 2026

## What the pages explain

A daily permit covers one selected date. Another parking day needs its own
day-pass purchase unless a valid term permit or campus exemption covers it.
The campus picker now labels both term and day prices. The fee table labels
the YCCD catalog's zero-price option **$0 FREE (term only)**, alongside its
separate **$2 (parking lot dispensers)** daily listing. The shared explanation
appears in campus selection/search, the planner, both application paths,
total review and resumed checkout. Catalog amounts and payment logic are unchanged.

Campus policy still controls whether someone needs a permit. The
[published vendor catalog](https://mycampuspermit.com/yccd.html) lists free
student permits and a separate $2 day pass. However,
[MJC's current parking page](https://mjc.edu/safety/parking.html), checked
October 24, 2026, says students pay no parking fees and need no permit in
student lots. Solvia displays that specific exemption, including at MJC
review and checkout. It does not tell those exempt students to buy a daily
permit. This MJC statement is not presented as a policy for every YCCD campus.
The shipped CSV remains a dated research snapshot, not a live policy feed.

## Reversible cleanup of the local SQLite demo

At the user's request, 12 old, unissued test applications were removed from
the local `src/Solvia/solvia.db`: **41 applications became 29**. No records
were removed from PayPal. The removed drafts had both statuses `Pending`,
were submitted before October 22 UTC, and had no order ID, capture ID,
paid timestamp, receipt, issuance timestamp or verification token. Their
original cause is not inferred from their pending status.

A frozen allowlist was rechecked in a locked transaction. A consistent
SQLite backup was created first with SQLite's backup API and verified
against the database before deletion. Every surviving application and every
other table was compared with its original rows; both foreign-key and
integrity checks passed.

Preserved records:

| Records | Count |
|---|---:|
| Paid applications | 18 |
| Free/exempt applications | 6 |
| Failed applications with provider references | 2 |
| Pending applications with an order ID | 2 |
| New October 24 pending draft | 1 |
| People | 6 |
| Vehicles | 41 |

Vehicles were preserved because their creation provenance is insufficient
to identify disposable test-only vehicles. The full backup and private
deleted-row manifest remain under ignored
`.playwright-data/backups/draft-cleanup-20261024T203555Z/`; neither is published.

## Verification

Verification results are recorded in [Testing Solvia](../TESTING.md).
The focused backend scope passed 64/64. Thirty desktop/phone smoke identities
passed; the expanded affected journey passed 2/2 again after the exemption
review. Four tablet navigation emulations also passed. The rerun overlaps the
30, so the distinct controlled browser scope is 34, not 36. The broader suite
was not rerun for this update.
The daily-price regression checks preserve the distinction between a zero
term fee and a 200-cent catalog day pass. Browser checks verify the visible
labels and exemption guidance without approving a payment. This update
does not claim a new live-provider payment or physical-device test.
