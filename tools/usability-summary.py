#!/usr/bin/env python3
"""Describe one provenance of observed paired tasks; no network/dependencies.

Synthetic validation needs --allow-synthetic and is never reported as a study.
Agent timing is labeled diagnostic; it is never converted to human evidence.
This tool reads a CSV and prints a report. It does not modify source data.
"""
from __future__ import annotations

import argparse
import csv
import math
from collections import Counter, defaultdict
from datetime import date, datetime
from decimal import Decimal, InvalidOperation
from pathlib import Path
from statistics import median


REQUIRED = {
    "provenance", "study_id", "participant_id", "role_group", "sequence",
    "task_variant", "condition", "observed_at_utc", "app_commit", "device_class",
    "campus", "permit_kind", "start_date", "end_date", "reader_source",
    "task_status", "elapsed_seconds", "first_draft_wrong_fields",
    "corrective_edits", "unresolved_wrong_fields", "help_prompts", "help_seconds",
    "expected_amount_usd", "stated_amount_usd", "payment_answer_correct",
    "sandbox_answer_correct", "abort_reason", "notes",
}
COUNTS = ("first_draft_wrong_fields", "corrective_edits",
          "unresolved_wrong_fields", "help_prompts")
COMPARABLE = ("role_group", "app_commit", "device_class", "campus",
              "permit_kind", "start_date", "end_date", "expected_amount_usd")


def number(value: str, field: str, *, integer=False, optional=False):
    if not value and optional:
        return None
    try:
        parsed = float(value)
    except ValueError:
        raise ValueError(f"{field} must be a number") from None
    if not math.isfinite(parsed) or parsed < 0 or (integer and not parsed.is_integer()):
        raise ValueError(f"{field} must be a finite nonnegative {'integer' if integer else 'number'}")
    return int(parsed) if integer else parsed


def money(value: str, field: str, optional=False):
    if not value and optional:
        return None
    try:
        parsed = Decimal(value)
        if not parsed.is_finite() or parsed < 0 or parsed != parsed.quantize(Decimal("0.01")):
            raise InvalidOperation
    except InvalidOperation:
        raise ValueError(f"{field} must be a nonnegative USD amount with at most two decimals") from None
    return parsed


def boolean(value: str, field: str, optional=False):
    if not value and optional:
        return None
    if value not in {"true", "false"}:
        raise ValueError(f"{field} must be true or false")
    return value == "true"


def parse_row(raw, line):
    if None in raw:
        raise ValueError(f"line {line}: unexpected extra CSV values")
    row = {key: (value or "").strip() for key, value in raw.items()}
    try:
        for field in ("provenance", "study_id", "participant_id", "role_group",
                      "task_variant", "app_commit", "device_class", "campus"):
            if not row[field]:
                raise ValueError(f"{field} is required")
        if row["provenance"] not in {"human", "agent", "synthetic"}:
            raise ValueError("provenance must be human, agent or synthetic")
        if row["condition"] not in {"ai", "classic"}:
            raise ValueError("condition must be ai or classic")
        if row["sequence"] not in {"1", "2"}:
            raise ValueError("sequence must be 1 or 2")
        stamp = datetime.fromisoformat(row["observed_at_utc"].replace("Z", "+00:00"))
        if stamp.utcoffset() is None or stamp.utcoffset().total_seconds() != 0:
            raise ValueError("observed_at_utc requires an explicit UTC offset/Z")
        if date.fromisoformat(row["end_date"]) < date.fromisoformat(row["start_date"]):
            raise ValueError("end_date precedes start_date")
        if row["permit_kind"] not in {"term", "day"}:
            raise ValueError("permit_kind must be term or day")
        if row["task_status"] not in {"completed", "timeout", "abandoned", "technical_failure"}:
            raise ValueError("invalid task_status")
        if row["reader_source"] not in {"together", "ollama", "fallback", "none"}:
            raise ValueError("invalid reader_source")
        if row["condition"] == "classic" and row["reader_source"] != "none":
            raise ValueError("classic entry must use reader_source=none")
        if row["condition"] == "ai" and row["task_status"] == "completed" and row["reader_source"] == "none":
            raise ValueError("completed AI entry must identify its actual reader/fallback")
        optional = row["task_status"] != "completed"
        for field in COUNTS:
            row[field] = number(row[field], field, integer=True, optional=optional)
        for field in ("elapsed_seconds", "help_seconds"):
            row[field] = number(row[field], field, optional=optional)
        row["expected_amount_usd"] = money(row["expected_amount_usd"], "expected_amount_usd")
        row["stated_amount_usd"] = money(row["stated_amount_usd"], "stated_amount_usd", optional=optional)
        for field in ("payment_answer_correct", "sandbox_answer_correct"):
            row[field] = boolean(row[field], field, optional=optional)
        row["correct_completion"] = (
            row["task_status"] == "completed" and row["unresolved_wrong_fields"] == 0
            and row["stated_amount_usd"] == row["expected_amount_usd"]
        )
    except (KeyError, ValueError) as error:
        raise ValueError(f"line {line}: {error}") from None
    return row


def describe(values):
    if not values:
        return "no observed values"
    return f"median {median(values):.1f}s; range {min(values):.1f}-{max(values):.1f}s; n={len(values)}"


def report(path: Path, allow_synthetic: bool):
    with path.open(newline="", encoding="utf-8-sig") as handle:
        reader = csv.DictReader(handle)
        if missing := REQUIRED - set(reader.fieldnames or []):
            raise ValueError("missing columns: " + ", ".join(sorted(missing)))
        rows = [parse_row(row, line) for line, row in enumerate(reader, start=2)
                if any(value and str(value).strip() for value in row.values())]
    if not rows:
        return "No observed tasks. This blank template contains no study results."
    provenance = {row["provenance"] for row in rows}
    if len(provenance) != 1:
        raise ValueError("mixed human/agent/synthetic provenance is forbidden; use separate files")
    provenance = provenance.pop()
    if provenance == "synthetic" and not allow_synthetic:
        raise ValueError("synthetic data require --allow-synthetic and are not study results")
    if len({row["study_id"] for row in rows}) != 1:
        raise ValueError("use one study_id per report")
    pairs = defaultdict(dict)
    for row in rows:
        pair = pairs[row["participant_id"]]
        if row["condition"] in pair:
            raise ValueError("duplicate participant/condition; retain the first outcome, not a favorable retry")
        pair[row["condition"]] = row
    for person, pair in pairs.items():
        if len(pair) == 2:
            first, second = pair["ai"], pair["classic"]
            if first["sequence"] == second["sequence"]:
                raise ValueError(f"{person}: paired sequence numbers must differ")
            if first["task_variant"] == second["task_variant"]:
                raise ValueError(f"{person}: use different matched vehicle variants")
            if any(first[field] != second[field] for field in COMPARABLE):
                raise ValueError(f"{person}: paired campus/type/date/device/version/role/price mismatch")
    title = {
        "human": "OBSERVED HUMAN PILOT - descriptive only; not population or causal evidence",
        "agent": "AGENT TASK DIAGNOSTICS - not human participants or measured human time savings",
        "synthetic": "SYNTHETIC VALIDATION FIXTURE - NOT OBSERVATIONS OR STUDY RESULTS",
    }[provenance]
    lines = [title, f"Study: {rows[0]['study_id']}; unique coded subjects/reviewers: {len(pairs)}; tasks: {len(rows)}"]
    lines.append("Role counts: " + str(dict(Counter(next(iter(pair.values()))["role_group"] for pair in pairs.values()))))
    lines.append("First-path counts: " + str(dict(Counter(row["condition"] for row in rows if row["sequence"] == "1"))))
    for condition in ("ai", "classic"):
        subset = [row for row in rows if row["condition"] == condition]
        good = [row for row in subset if row["correct_completion"]]
        lines += ["", f"{condition.upper()}: correct checkout {len(good)}/{len(subset)} attempted tasks",
                  f"Statuses (failures retained): {dict(Counter(row['task_status'] for row in subset))}",
                  "Correct-completion time only: " + describe([row["elapsed_seconds"] for row in good]),
                  f"Readers: {dict(Counter(row['reader_source'] for row in subset))}"]
        for field in ("payment_answer_correct", "sandbox_answer_correct"):
            observed = [row[field] for row in subset if row[field] is not None]
            lines.append(f"{field}: {sum(observed)}/{len(observed)} observed answers correct; {len(subset)-len(observed)} missing")
        for field in COUNTS:
            observed = [row[field] for row in subset if row[field] is not None]
            lines.append(f"{field}: total {sum(observed)} across {len(observed)} observed tasks; {len(subset)-len(observed)} missing")
        lines.append("Observed support time: " + describe([row["help_seconds"] for row in subset if row["help_seconds"] is not None]))
    complete = [pair for pair in pairs.values() if len(pair) == 2]
    good_pairs = [pair for pair in complete if all(row["correct_completion"] for row in pair.values())]
    deltas = [pair["classic"]["elapsed_seconds"] - pair["ai"]["elapsed_seconds"] for pair in good_pairs]
    lines += ["", f"Complete pairs: {len(complete)}/{len(pairs)}; both-correct pairs: {len(good_pairs)}",
              "Paired classic-minus-AI seconds, both-correct survivors only: " + describe(deltas),
              "Positive paired differences mean this observed AI task was faster. Negative differences mean classic was faster.",
              "Survivor-only timing omits failed tasks; read alongside completion counts. No ROI, human savings or significance claim.",
              "PDF retrieval uses its own observation sheet and is not attributed to the entry condition."]
    if len(complete) != len(pairs):
        lines.append("WARNING: incomplete pairs retained; do not silently exclude their attempted tasks.")
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("csv_file", type=Path)
    parser.add_argument("--allow-synthetic", action="store_true")
    args = parser.parse_args()
    try:
        print(report(args.csv_file, args.allow_synthetic))
    except (OSError, ValueError, csv.Error) as error:
        parser.exit(2, f"Cannot summarize: {error}\n")


if __name__ == "__main__":
    main()
