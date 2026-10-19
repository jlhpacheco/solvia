# Fee refresh — the KERNEL-style browser job that keeps fees.csv honest

Solvia's prices are real published college fees, scanned by hand into
`data/fees.csv` (the 10 campuses that publish a student semester price,
from Parking Management Bureau pages at mycampuspermit.com — the
2026-10-18 scan covered 25 pages; the 15 that publish no semester
price are not in the app). Fees change. This tool is the refresh job: it
**re-walks the same public pages**, extracts the price lines, and
diffs them against the fees.csv baseline.

In the sponsor plan (`docs/SPONSORS.md`) this job belongs to
**KERNEL** — cloud browser infrastructure for AI agents (durable
sessions, replays) is the production runner for exactly this kind of
re-walk. `fee-refresh.sh` is the same job written in portable
**bash + curl**, so it runs anywhere with no account: same sources,
same diff, same report. Running it here does not require KERNEL; the
repo claims the script and its output, and names KERNEL as the
job's production runner — nothing more. Honestly framed: this is a
portable offline ops script, not a KERNEL integration.

## The one rule: it PROPOSES, never edits

The script only **reads** `data/fees.csv` and prints a review report.
It never writes the file. A human reads the report, verifies any
change on the college's own page, and edits `fees.csv` by hand.

## Files

| File | What it is |
|---|---|
| `fee-refresh.sh` | The refresh job (bash + curl). Read-only against the repo |
| `sources.csv` | The 10 campus pages the fee table was built from (campus,url) — taken from the Source notes in `data/fees.csv`, one row per campus in the redo table |

The 10 campuses in `sources.csv` are exactly the 10 rows of the redo
`data/fees.csv`: CSU Monterey Bay, CSU East Bay, CSU Bakersfield,
Modesto Junior College / Columbia College / YCCD, Contra Costa
College, Diablo Valley College, Los Medanos College, Antelope Valley
College, State Center Community College District, and Fresno City
College. Campus names match the `fees.csv` first field exactly (the
script anchors its baseline lookup to the start of each row), and
every URL is the page named in that row's Source note.

## Run it

```bash
tools/fee-refresh/fee-refresh.sh              # all 10 campuses
tools/fee-refresh/fee-refresh.sh --limit 3    # first 3 sources
tools/fee-refresh/fee-refresh.sh --campus "Fresno"   # name filter
```

For each campus it prints the baseline amounts found in `fees.csv`,
the amounts found on the live page, and one verdict:

| Verdict | Meaning |
|---|---|
| `UNCHANGED` | Every baseline amount still appears on the page |
| `CHANGED` | Page fetched, but a baseline amount is gone — the report prints the page's current price lines for a human to review |
| `UNREACHABLE` | The page could not be fetched (HTTP status / curl error printed). Several PMB pages were already HTTP 502 when the table was researched — the report says so instead of guessing |
| `NO BASELINE` | `fees.csv` lists no dollar amount for this campus (nothing published) — nothing to compare |

Fetching note: the PMB host's CDN answers a bare first request with
a 307 challenge redirect plus a cookie, so the script keeps a
per-campus cookie jar — the same thing a browser does. The 307 is
occasionally returned as the final answer anyway; that shows up as
`UNREACHABLE … HTTP 307` and a retry usually fetches the page. The
pages are public, nothing is logged into, and the tool's user agent
names it (`SolviaFeeRefresh/1.0`).

## Sample run (real output, Oct 19, 2026)

`fee-refresh.sh --limit 3`, run from this repo (the solvia-redo tree):

```text
==============================================================
 Solvia fee refresh — 2026-10-19 04:16 UTC
 Sources : tools/fee-refresh/sources.csv
 Baseline: data/fees.csv (read-only — this tool never edits it)
==============================================================

[1] CSU Monterey Bay
    https://mycampuspermit.com/mbay.html
    fees.csv amounts: 100.00 153.00 2.00 229.50 459.00 5.00 612.00 86.00
    page amounts:     100.00 119.07 135.00 14.89 153.00 158.76 180.00 2.00 204.00 229.50 24.75 33.75 39.69 45.00 459.00 5.00 51.00 59.53 612.00 67.50 76.50 86.00
    UNCHANGED — every fees.csv amount still appears on the page.

[2] CSU East Bay
    https://mycampuspermit.com/eastbay.html
    fees.csv amounts: 10.00 100.00 195.00 2.00 50.00 97.50
    page amounts:     10.00 100.00 195.00 2.00 50.00 97.50
    UNCHANGED — every fees.csv amount still appears on the page.

[3] CSU Bakersfield
    https://mycampuspermit.com/bakersfield.html
    fees.csv amounts: 22.00 6.00 93.00
    page amounts:     22.00 6.00 93.00
    UNCHANGED — every fees.csv amount still appears on the page.

==============================================================
 Summary: 3 checked — 3 unchanged · 0 changed
 (review) · 0 unreachable · 0 no baseline.
 This report PROPOSES changes only — data/fees.csv was not
 modified. A human verifies any CHANGED campus on the college's
 own page before editing fees.csv by hand.
==============================================================
```

(Absolute paths in the header lines are trimmed above; the verdicts,
amounts, and summary are verbatim. An earlier attempt the same
morning at 04:15 UTC returned `UNREACHABLE — HTTP 307` for CSU
Monterey Bay — the CDN challenge described above; the retry at
04:16 UTC fetched it and reported UNCHANGED. Spot checks the same
morning also returned UNCHANGED for Diablo Valley College, Los
Medanos College, Modesto Junior College / Columbia College / YCCD,
State Center Community College District, and Fresno City College.
`data/fees.csv`'s SHA-256 before and after the run:
`327aca14c33f99a1ffd412ccdbb8ec434b02c1cb440c17d2e5ce5edfabe5771c`
— identical, as the read-only rule requires. That hash is the
10-campus table; the earlier 25-campus scan file lives in the
research record, not in this repo's data.)
