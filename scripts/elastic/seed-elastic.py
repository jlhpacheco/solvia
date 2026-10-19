#!/usr/bin/env python3
"""Seed the local Elasticsearch `colleges` index from data/fees.csv.

Solvia T8 (stretch). Python 3 standard library only — nothing to
install. Usage:

    python3 scripts/elastic/seed-elastic.py [path/to/fees.csv]

Environment:
    ELASTIC_URL    default http://localhost:9200
    ELASTIC_INDEX  default colleges

What it does: deletes the index if present, recreates it from
colleges.mapping.json (next to this script), bulk-loads one document
per campus (10), refreshes, and prints the document count.

Price honesty: the cent values are parsed with the SAME rules as the
app's FeeCatalogService — "not listed" stays null (the field is left
out of the document, never 0), a published FREE is a real 0 with
isFree=true, and nothing is ever invented. The index is a search
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

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
ELASTIC_URL = os.environ.get("ELASTIC_URL", "http://localhost:9200").rstrip("/")
INDEX = os.environ.get("ELASTIC_INDEX", "colleges")

MONEY = re.compile(r"\$(\d{1,3}(?:,\d{3})*(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)")


def first_money_cents(text):
    m = MONEY.search(text)
    if not m:
        return None
    return int(round(float(m.group(1).replace(",", "")) * 100))


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


def slug(name):
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


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


def main():
    csv_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        REPO_ROOT, "data", "fees.csv")
    mapping_path = os.path.join(SCRIPT_DIR, "colleges.mapping.json")

    with open(csv_path, newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    if len(rows) != 10:
        print(f"WARNING: expected 10 campus rows, found {len(rows)}")

    docs = []
    for row in rows:
        campus = (row.get("Campus") or "").strip()
        if not campus:
            continue
        semester = row.get("Student semester fee") or ""
        other = row.get("Other term fees") or ""
        day = row.get("Day pass") or ""
        moto = row.get("Motorcycle") or ""
        terms = row.get("Terms/dates listed") or ""
        source = row.get("Source note") or ""
        fall, spring, is_free = parse_semester(semester)
        summer = parse_summer(other)

        doc = {
            "campus": campus,
            "searchText": " ".join(
                [campus, semester, other, day, moto, terms]).strip(),
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
        docs.append(doc)

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
    main()
