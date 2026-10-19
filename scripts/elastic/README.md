# Elastic college search (Solvia T8, stretch)

Solvia's pick-your-college screen talks to **one** search contract,
`ICollegeSearch` (`src/Solvia/Services/ICollegeSearch.cs`), with two
providers:

| Provider | What it is | When it serves |
|---|---|---|
| `builtin` (default, shipped) | Deterministic in-memory match over the 10 fee rows (`BuiltinCollegeSearch`) | Always available; needs no server; also the fallback for the Elastic provider |
| `elastic` | A local Elasticsearch `colleges` index, hybrid keyword `multi_match` over `campus^3` + `searchText` (`ElasticCollegeSearch`) | Only when config `Search:Provider` is `elastic` |

Whichever provider answers, the rows and prices are the catalog's:
Elastic only finds and ranks campus names, and every hit is resolved
back through `IFeeCatalog`. The screen also prints which provider
served the results, and if the Elastic node is down the app falls
back to the built-in match and says so. **docs/SPONSORS.md states
which mode actually ran** — read it before claiming anything.

## Run the Elastic mode locally

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
   FREE is a real 0). Check it:

   ```bash
   curl "http://localhost:9200/colleges/_count"
   curl -X POST "http://localhost:9200/colleges/_search" \
     -H "Content-Type: application/json" \
     -d '{"size": 5, "query": {"multi_match":
          {"query": "Fresno", "fields": ["campus^3", "searchText"]}}}'
   ```

   "Fresno" returns Fresno City College first.

3. **Point the app at it** (environment variables, or the `Search`
   and `Elastic` sections in `appsettings.json`):

   ```bash
   Search__Provider=elastic Elastic__Url=http://localhost:9200 dotnet run --project src/Solvia
   ```

4. Open **Pick your college**, search `Fresno`. The screen labels the
   results "served by Elastic (local colleges index)". Stop the
   Elasticsearch container and search again: the same screen answers
   with the built-in results and labels them "the built-in search" —
   the fallback is part of the design, not an error page.

## ELSER (optional, not shipped)

`colleges.mapping.with-elser.json` adds a `semantic_text` field
backed by ELSER — Elastic's own trained sparse-encoder model — via
the `.elser-2-elasticsearch` inference endpoint. It is **not** part
of the default setup: the endpoint needs a running ML node (Elastic
recommends ~8 GB for ELSER) and may need a trial/subscription
depending on distribution, and the app does not query the field.
Elastic Rerank is likewise not used and never claimed. See
`docs/SPONSORS.md` for the exact mode the submitted build ran.
