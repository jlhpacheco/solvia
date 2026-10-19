#!/usr/bin/env python3
"""Seed the local Elasticsearch `colleges` index from data/fees.csv.

Solvia REDO R9 — port of the frozen v1 script (solvia-merge
scripts/elastic/seed-elastic.py), retargeted to the redo catalog:
data/fees.csv as shipped in the redo (header + exactly 10 campuses
that publish a semester price) and the redo CampusFee field set,
which adds DayPassCents over v1. Python 3 standard library only —
nothing to install. Usage:

    python3 scripts/elastic/seed-elastic.py [path/to/fees.csv]
    python3 scripts/elastic/seed-elastic.py --dry-run [path/to/fees.csv]

    --dry-run parses the CSV, prints the documents it WOULD load
    (one line per campus with every parsed cent value), and exits
    without contacting any Elastic node. That is the mode used to
    verify the fee parse in the build sandbox, where no live node
    has ever run (see README.md in this folder).

Environment:
    ELASTIC_URL    default http://localhost:9200
    ELASTIC_INDEX  default colleges
(These feed the script only. The app reads the same values from its
own config: Elastic:Url / Elastic:Index — see README.md.)

What it does (live mode): deletes the index if present, recreates
it from colleges.mapping.json (next to this script), bulk-loads one
document per campus (10), refreshes, and prints the document count.

Document shape — exactly the redo CampusFee fields the index
carries: campus, searchText (the campus name plus the catalog's fee
texts — the same texts BuiltinCollegeSearch matches over), the
parsed prices fallSemesterCents / springSemesterCents /
summerSemesterCents / dayPassCents, isFree,
hasPublishedSemesterPrice, and sourceNote.

Price honesty: the cent values are parsed with the SAME rules as
the redo app's FeeCatalogService — "not listed" stays null (the
field is left out of the document, never 0), a published FREE is a
real 0 with isFree=true, the day-pass parse is label-aware (an
amount explicitly labelled "/day" / "per day" / "a day" beats an
earlier hourly amount — CSU East Bay "$2/hour or $10/day" parses to
1000, not 200), and nothing is ever invented. The index is a search
copy only: the app resolves every hit back through its catalog, so
these prices are never the ones quoted or charged.
"""

import csv
import json
import os
import re
import sys
import urllib.error
import urllib.request
from decimal import Decimal, ROUND_HALF_UP

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
ELASTIC_URL = os.environ.get("ELASTIC_URL", "http://localhost:9200").rstrip("/")
INDEX = os.environ.get("ELASTIC_INDEX", "colleges")

MONEY = re.compile(r"\$(\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)")


def money_to_cents(amount_text):
    """Dollar text -> cents, decimal-exact like the C# catalog
    (decimal.Parse + round half away from zero)."""
    value = Decimal(amount_text.replace(",", "")) * 100
    return int(value.quantize(Decimal("1"), rounding=ROUND_HALF_UP))


def first_money_cents(text):
    m = MONEY.search(text)
    if not m:
        return None
    return money_to_cents(m.group(1))


def parse_semester(cell):
    """Mirror of FeeCatalogService.ParseSemesterFees -> (fall, spring, is_free)."""
    cell = (cell or "").strip()
    if not cell or cell.lower().startswith("not listed"):
        return None, None, False
    if cell.lower().startswith("free"):
        return 0, 0, True
    fall = spring = None
    for segment in cell.split(";"):
        cents = first_money_cents(segment)
        if cents is None:
            continue
        has_fall = "fall" in segment.lower()
        has_spring = "spring" in segment.lower()
        if has_fall and fall is None:
            fall = cents
        if has_spring and spring is None:
            spring = cents
        if not has_fall and not has_spring:
            if fall is None:
                fall = cents
            if spring is None:
                spring = cents
    return fall, spring, False


def parse_summer(other_cell):
    """Mirror of FeeCatalogService.ParseSummerFee."""
    cell = (other_cell or "").strip()
    if not cell or cell.lower().startswith("not listed"):
        return None
    for segment in cell.split(";"):
        if "summer" in segment.lower():
            return first_money_cents(segment)
    return None


def parse_day_pass(day_cell):
    """Mirror of FeeCatalogService.ParseDayPassCents (label-aware).

    Reads only the FIRST ';' segment (later segments are staff /
    other-audience rates). Inside it, an amount explicitly labelled
    as the day rate ("/day", "per day", "a day" right after the
    amount) beats an earlier unlabelled or hourly amount; otherwise
    the first amount wins. "not listed" / no amount -> None.
    CSU East Bay "$2/hour or $10/day (dispensers)" -> 1000.
    CSU Bakersfield "$6/day" -> 600.
    """
    cell = (day_cell or "").strip()
    if not cell or cell.lower().startswith("not listed"):
        return None
    segment = cell.split(";")[0]
    first = None
    for m in MONEY.finditer(segment):
        cents = money_to_cents(m.group(1))
        if first is None:
            first = cents
        after = segment[m.end():].lstrip().lower()
        if (after.startswith("/day") or after.startswith("per day")
                or after.startswith("a day")):
            return cents
    return first


def slug(name):
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def build_documents(rows):
    """CSV rows -> the exact documents the index carries (redo field set)."""
    docs = []
    for row in rows:
        campus = (row.get("Campus") or "").strip()
        if not campus:
            continue
        semester = row.get("Student semester fee") or ""
        other = row.get("Other term fees") or ""
        day = row.get("Day pass") or ""
        moto = row.get("Motorcycle") or ""
        source = row.get("Source note") or ""
        fall, spring, is_free = parse_semester(semester)
        summer = parse_summer(other)
        day_pass = parse_day_pass(day)

        doc = {
            "campus": campus,
            # The redo catalog's searchable texts: the same fields
            # BuiltinCollegeSearch matches over (CampusFee's text
            # fields). The CSV's "Terms/dates listed" column is not
            # a catalog field in the redo, so it is not indexed.
            "searchText": " ".join(
                [campus, semester, other, day, moto, source]).strip(),
            "isFree": is_free,
            "hasPublishedSemesterPrice": fall is not None or spring is not None,
            "sourceNote": source,
        }
        if fall is not None:
            doc["fallSemesterCents"] = fall
        if spring is not None:
            doc["springSemesterCents"] = spring
        if summer is not None:
            doc["summerSemesterCents"] = summer
        if day_pass is not None:
            doc["dayPassCents"] = day_pass
        docs.append(doc)
    return docs


def http(method, path, body=None, content_type="application/json"):
    data = None
    if body is not None:
        data = body if isinstance(body, bytes) else body.encode("utf-8")
    req = urllib.request.Request(
        ELASTIC_URL + path, data=data, method=method,
        headers={"Content-Type": content_type} if data else {})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return resp.status, resp.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")


def main(argv):
    args = [a for a in argv[1:] if a != "--dry-run"]
    dry_run = len(args) != len(argv[1:])
    csv_path = args[0] if args else os.path.join(REPO_ROOT, "data", "fees.csv")
    mapping_path = os.path.join(SCRIPT_DIR, "colleges.mapping.json")

    with open(csv_path, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    if len(rows) != 10:
        print(f"WARNING: expected 10 campus rows, found {len(rows)}")

    docs = build_documents(rows)

    if dry_run:
        print(f"DRY RUN — no Elastic node contacted ({ELASTIC_URL})")
        print(f"Parsed {len(docs)} campuses from {csv_path}:")
        for d in docs:
            print(
                f"  {d['campus']}: "
                f"fall={d.get('fallSemesterCents')} "
                f"spring={d.get('springSemesterCents')} "
                f"summer={d.get('summerSemesterCents')} "
                f"dayPass={d.get('dayPassCents')} "
                f"isFree={d['isFree']} "
                f"hasPublishedSemesterPrice={d['hasPublishedSemesterPrice']}")
        print(json.dumps(docs, indent=2))
        return

    # Recreate the index from the shipped mapping.
    status, _ = http("DELETE", f"/{INDEX}")
    print(f"DELETE /{INDEX} -> {status} (404 is fine on first run)")
    with open(mapping_path, encoding="utf-8") as f:
        mapping = f.read()
    status, body = http("PUT", f"/{INDEX}", mapping)
    print(f"PUT /{INDEX} -> {status}")
    if status >= 300:
        print(body)
        sys.exit(1)

    ndjson = "".join(
        json.dumps({"index": {"_index": INDEX, "_id": slug(d["campus"])}})
        + "\n" + json.dumps(d) + "\n"
        for d in docs)
    status, body = http(
        "POST", "/_bulk?refresh=true", ndjson,
        content_type="application/x-ndjson")
    print(f"POST /_bulk ({len(docs)} docs) -> {status}")
    if status >= 300 or json.loads(body).get("errors"):
        print(body)
        sys.exit(1)

    status, body = http("GET", f"/{INDEX}/_count")
    print(f"GET /{INDEX}/_count -> {status} {body.strip()}")
    print(f"Seeded {len(docs)} campuses into '{INDEX}' at {ELASTIC_URL}")


if __name__ == "__main__":
    main(sys.argv)
