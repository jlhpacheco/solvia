#!/usr/bin/env bash
# =============================================================================
# Solvia fee-refresh — a KERNEL-style browser refresh job for the fee table
# =============================================================================
#
# WHAT THIS IS
#   The job KERNEL's cloud-browser agent runs for Solvia (SPONSOR-MAP:
#   KERNEL = fee-refresh browser agent): re-walk the public campus
#   permit pages that data/fees.csv was built from, extract the price
#   lines, and diff them against the fees.csv baseline. KERNEL is the
#   production runner for this job (durable browser sessions, replays);
#   this script is the same job in portable bash + curl so anyone can
#   run it anywhere with no account.
#
# WHAT IT DOES
#   For each campus in sources.csv (campus,url):
#     1. fetch the public page with curl,
#     2. strip the HTML and extract every dollar amount + price line,
#     3. compare the amounts against that campus's fees.csv row.
#   And print one human review report:
#     UNCHANGED    every baseline amount still appears on the page
#     CHANGED      page fetched, but a baseline amount is gone — a
#                  human reviews the printed price lines
#     UNREACHABLE  the page could not be fetched (HTTP status / error
#                  is printed; several PMB pages were already 502 when
#                  the table was researched)
#     NO BASELINE  fees.csv lists no dollar amount for this campus
#                  (nothing published / nothing to compare)
#
# WHAT IT NEVER DOES
#   It PROPOSES changes only. It never edits data/fees.csv — a person
#   reads this report, checks the college's page, and edits fees.csv
#   by hand. The script writes nothing except temp files it deletes.
#
# USAGE
#   tools/fee-refresh/fee-refresh.sh [--limit N] [--campus "Fresno"]
#     --limit N       check only the first N sources (default: all 10)
#     --campus TEXT   check only campuses whose name contains TEXT
# =============================================================================

set -u

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
FEES_CSV="$REPO_ROOT/data/fees.csv"
SOURCES="$SCRIPT_DIR/sources.csv"
UA="SolviaFeeRefresh/1.0 (fee-table refresh; github.com/jlhpacheco/solvia)"

LIMIT=0
CAMPUS_FILTER=""

while [ $# -gt 0 ]; do
  case "$1" in
    --limit)  LIMIT="${2:-0}"; shift 2 ;;
    --campus) CAMPUS_FILTER="${2:-}"; shift 2 ;;
    *) echo "Unknown argument: $1" >&2; exit 2 ;;
  esac
done

[ -f "$FEES_CSV" ] || { echo "Baseline not found: $FEES_CSV" >&2; exit 2; }
[ -f "$SOURCES" ]  || { echo "Sources not found: $SOURCES" >&2; exit 2; }

# Extract every dollar amount from stdin, normalized to 2 decimals,
# unique + sorted. "$5" and "$5.00" are the same amount here.
amounts() {
  grep -oE '\$[0-9][0-9,]*(\.[0-9]{1,2})?' \
    | tr -d '$,' \
    | awk '{ printf "%.2f\n", $1 }' \
    | sort -u
}

echo "=============================================================="
echo " Solvia fee refresh — $(date -u '+%Y-%m-%d %H:%M UTC')"
echo " Sources : $SOURCES"
echo " Baseline: $FEES_CSV (read-only — this tool never edits it)"
echo "=============================================================="

checked=0; n_unchanged=0; n_changed=0; n_unreachable=0; n_nobaseline=0

# sources.csv rows are simple (campus names contain no commas).
# Process substitution (not a pipeline) keeps the loop in THIS shell,
# so the summary counters below are real.
while IFS=, read -r campus url; do
  # Tolerate CRLF sources files: a stray CR in the URL makes curl
  # reject it as malformed (exit 3).
  campus="${campus%$'\r'}"
  url="${url%$'\r'}"
  [ -n "$campus" ] || continue
  if [ -n "$CAMPUS_FILTER" ]; then
    case "$campus" in *"$CAMPUS_FILTER"*) : ;; *) continue ;; esac
  fi
  if [ "$LIMIT" -gt 0 ] && [ "$checked" -ge "$LIMIT" ]; then
    break
  fi
  checked=$((checked + 1))

  echo
  echo "[$checked] $campus"
  echo "    $url"

  # Baseline amounts: the campus's own row in fees.csv (rows are
  # single-line; the campus name is the first field and has no comma).
  # Anchored to line start — a campus name can also appear inside
  # ANOTHER row's source note, and that row is not the baseline.
  baseline="$(awk -v c="$campus" 'index($0, c ",") == 1' "$FEES_CSV" | head -1 | amounts | tr '\n' ' ')"
  if [ -z "${baseline// }" ]; then
    echo "    NO BASELINE — fees.csv lists no dollar amount for this campus; nothing to compare."
    n_nobaseline=$((n_nobaseline + 1))
    continue
  fi
  echo "    fees.csv amounts: $baseline"

  # Fetch with a per-campus cookie jar: the PMB host's CDN answers a
  # bare first request with a 307 challenge redirect + a cookie, and
  # only the follow-up request carrying that cookie gets the page —
  # exactly what a browser does. (No logins, nothing bypassed: these
  # are public pages and the UA names this tool.)
  tmp="$(mktemp)"
  jar="$(mktemp)"
  http_code="$(curl -sL --max-time 30 -A "$UA" -c "$jar" -b "$jar" -o "$tmp" -w '%{http_code}' "$url" 2>/dev/null)"
  curl_rc=$?
  if [ "$curl_rc" -ne 0 ] || [ "$http_code" != "200" ]; then
    echo "    UNREACHABLE — curl exit $curl_rc, HTTP ${http_code:-none}. (The page may be down; fees.csv is untouched.)"
    rm -f "$tmp" "$jar"
    n_unreachable=$((n_unreachable + 1))
    continue
  fi

  page_text="$(sed -e 's/<[^>]*>/ /g' "$tmp")"
  page_amounts="$(printf '%s' "$page_text" | amounts | tr '\n' ' ')"
  rm -f "$tmp" "$jar"
  echo "    page amounts:     $page_amounts"

  missing=""
  for amt in $baseline; do
    case " $page_amounts " in
      *" $amt "*) : ;;
      *) missing="$missing $amt" ;;
    esac
  done

  if [ -z "$missing" ]; then
    echo "    UNCHANGED — every fees.csv amount still appears on the page."
    n_unchanged=$((n_unchanged + 1))
  else
    echo "    CHANGED — baseline amount(s) no longer on the page:$missing"
    echo "    Price lines now on the page (review these):"
    printf '%s' "$page_text" \
      | tr -s ' \t' ' ' \
      | grep -E '\$' \
      | sed -e 's/^ *//' -e 's/ *$//' \
      | grep -v '^$' \
      | head -6 \
      | sed 's/^/      /'
    n_changed=$((n_changed + 1))
  fi
done < <(tail -n +2 "$SOURCES")

echo
echo "=============================================================="
echo " Summary: $checked checked — $n_unchanged unchanged · $n_changed changed"
echo " (review) · $n_unreachable unreachable · $n_nobaseline no baseline."
echo " This report PROPOSES changes only — data/fees.csv was not"
echo " modified. A human verifies any CHANGED campus on the college's"
echo " own page before editing fees.csv by hand."
echo "=============================================================="
