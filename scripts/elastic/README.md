# Elastic college search (Solvia REDO R9)

Solvia's find-your-college screen talks to **one** search contract,
`ICollegeSearch` (`src/Solvia/Services/ICollegeSearch.cs`), with two
providers:

| Provider | What it is | When it serves |
|---|---|---|
| `builtin` (**default, shipped**) | Deterministic in-memory match over the catalog's 10 fee rows (`BuiltinCollegeSearch`) | Always available; needs no server; also the fallback for the Elastic provider |
| `elastic` | A local Elasticsearch `colleges` index, keyword `multi_match` over `campus^3` + `searchText` (`ElasticCollegeSearch`) | Only when config `Search:Provider` is set to `elastic` |

**The built-in search is the default, and it is what the app ships
with.** Elastic is an optional, config-selected provider — nothing
about the shipped app needs a search server. Whichever provider
answers, the rows and prices are the catalog's: Elastic only finds
and ranks campus names, and every hit is resolved back through
`IFeeCatalog`. If the Elastic node is down, times out, or answers
badly, the app falls back to the built-in match automatically — and
the screen says so: the results page prints **"Search served by:
builtin"** or **"Search served by: elastic"**, so a demo never
silently pretends Elastic answered when the built-in match did.
**docs/SPONSORS.md states which mode actually ran** — read it before
claiming anything.

## Honest status: no live Elastic node has run here

A live Elastic node has **never** run in the build sandbox. The
verified kill-bar from the v1 build: the official Elasticsearch
tarball is ~648 MB and the sandbox pipe measured ~0.9 MB/s, so the
download alone blew the attempt budget — and the sandbox also
intercepts app-process connections to localhost, so a true app →
node round trip could not be proven here either. What WAS proven
here is the seam: the provider contract, the config flag, the
automatic fallback, the on-screen provider label, and — via the seed
script's `--dry-run` mode (below) — that the index documents carry
exactly the redo catalog's parsed prices, cent for cent. **The steps
below are the live-run steps, to be run on Jose Luis's PC**, where
the node and the app can actually talk to each other.

## Check the seed data without any node (dry run)

Python 3, standard library only:

```bash
python3 scripts/elastic/seed-elastic.py --dry-run
```

This parses `data/fees.csv` (the redo's shipped file: header +
exactly 10 campuses that publish a semester price) with the same
rules as the app's `FeeCatalogService`, and prints one document per
campus — it contacts nothing. The parsed prices include the
label-aware day-pass parse (`dayPassCents`): CSU East Bay's
`"$2/hour or $10/day"` is **1000** cents (the day rate, not the $2
hourly rate in front of it), CSU Bakersfield's `"$6/day"` is **600**,
Fresno City College's semester price is **3000**, and the YCCD row's
published FREE semester price is a real **0** (with `isFree: true`) —
never confused with "not listed", which stays absent from the
document rather than becoming 0.

## Run the Elastic mode locally (on Jose Luis's PC)

1. **Start a single-node Elasticsearch** (free, local; security off
   for local dev only):

   ```bash
   docker run -d --name solvia-es -p 9200:9200 \
     -e discovery.type=single-node -e xpack.security.enabled=false \
     docker.elastic.co/elasticsearch/elasticsearch:8.18.1
   ```

   No Docker? Download the official tarball for your OS from
   elastic.co/downloads/elasticsearch, unpack, set the same two
   settings in `config/elasticsearch.yml`, and run
   `bin/elasticsearch`. Wait until `curl http://localhost:9200`
   answers.

2. **Create the index and load the 10 campuses** (Python 3, standard
   library only):

   ```bash
   python3 scripts/elastic/seed-elastic.py
   ```

   This recreates the `colleges` index from
   `colleges.mapping.json` and bulk-loads one document per campus
   from `data/fees.csv`, parsing fees with the same rules as the
   app's catalog ("not listed" stays absent — never 0; a published
   FREE is a real 0). The script reads `ELASTIC_URL` (default
   `http://localhost:9200`) and `ELASTIC_INDEX` (default `colleges`)
   from its environment. Check it:

   ```bash
   curl "http://localhost:9200/colleges/_count"
   curl -X POST "http://localhost:9200/colleges/_search" \
     -H "Content-Type: application/json" \
     -d '{"size": 5, "query": {"multi_match":
          {"query": "Fresno", "fields": ["campus^3", "searchText"]}}}'
   ```

   "Fresno" returns Fresno City College first, and `_count` says 10.

3. **Point the app at it** — the app's config keys are
   `Search:Provider`, `Elastic:Url`, and `Elastic:Index` (see
   `src/Solvia/appsettings.example.json`); as environment variables:

   ```bash
   Search__Provider=elastic Elastic__Url=http://localhost:9200 Elastic__Index=colleges dotnet run --project src/Solvia
   ```

4. Open **Find your college**, search `Fresno`. The screen labels the
   results "Search served by: **elastic**". Stop the Elasticsearch
   container and search again: the same screen answers with the
   built-in results and labels them "Search served by: **builtin**" —
   the fallback is part of the design, not an error page. Remove the
   `Search__Provider` override (or set it back to `builtin`) and the
   app is exactly the shipped default again.

## ELSER (optional, mapping file only — NOT claimed)

`colleges.mapping.with-elser.json` adds a `semantic_text` field
backed by ELSER — Elastic's own trained sparse-encoder model — via
the `.elser-2-elasticsearch` inference endpoint. It is a **mapping
file only**: it has never been run, the endpoint needs a running ML
node (Elastic recommends ~8 GB for ELSER) and may need a
trial/subscription depending on distribution, and the app does not
query the field. **ELSER is not claimed** — not in this README, not
in the app, not in docs/SPONSORS.md. Elastic Rerank is likewise not
used and never claimed. See `docs/SPONSORS.md` for the exact mode
the submitted build ran.
