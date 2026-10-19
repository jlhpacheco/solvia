# NOTICES — third-party licenses

Completed by the R10 checker (REDO-PLAN §3), verified against the
shipped tree: package references in `src/Solvia/Solvia.csproj` and
`tests/Solvia.Tests/Solvia.Tests.csproj`, the vendored libraries
under `src/Solvia/wwwroot/lib/`, the CDN reference in
`Views/Shared/_FeeTable.cshtml`, and the model/runtime the app
calls locally. If it is not in this tree, it is not listed here.

Solvia itself is MIT-licensed (see LICENSE at the repo root).
Copyright (c) 2026 Jose Luis Pacheco.

## Components the app ships or calls

- **QuestPDF 2026.2.4** — the permit receipt PDF
  (`PermitPdfService`). **QuestPDF Community License v3.0 — this
  is NOT the MIT License and NOT Apache.** The Community License
  is free for open-source projects published under an OSI-approved
  license (Solvia is MIT and publicly available, so it qualifies),
  for individuals, and for organizations under its revenue
  threshold; **public-sector entities are excluded** from the free
  Community terms. Solvia sets `QuestPDF.Settings.License =
  LicenseType.Community` in code and claims nothing more. Full
  terms: https://www.questpdf.com/license

- **Microsoft.EntityFrameworkCore.Sqlite 10.0.13** — MIT License.
  The app's only database provider (EF Core + SQLite, a single
  local file; there is no database server). SQLite itself is
  public domain.

- **Microsoft Phi-4-mini** — MIT License. The AI model. It is NOT
  bundled with this repo: it runs on the user's own machine,
  served by Ollama (`ollama pull phi4-mini`, about 2.5 GB).
  Microsoft is the model's author; the model card and license ship
  with the model, not with Solvia.

- **Ollama** — MIT License. External local runtime, NOT bundled:
  the app calls it over HTTP at `http://localhost:11434` when it
  is present, and uses a built-in deterministic fallback reader
  when it is not.

- **AG Grid Community 34.3.1** — MIT License. The fee-table grid.
  Free Community edition, loaded from the official jsDelivr CDN
  (`ag-grid-community@34.3.1`) by `Views/Shared/_FeeTable.cshtml`;
  a built-in renderer behind the same config flag shows identical
  values when the grid is off.

- **Bootstrap 5.3.3** — MIT License. Vendored under
  `wwwroot/lib/bootstrap/` with its license in the file headers.

- **jQuery** — MIT License. Vendored under `wwwroot/lib/jquery/`
  (LICENSE.txt in that folder).

- **jQuery Validation** — MIT License. Vendored under
  `wwwroot/lib/jquery-validation/` (LICENSE.md in that folder).

- **jQuery Validation Unobtrusive** — MIT License,
  Copyright (c) .NET Foundation and Contributors. Vendored under
  `wwwroot/lib/jquery-validation-unobtrusive/` (LICENSE.txt in
  that folder).

## Test-only components (never ship in the app)

- **xunit 2.9.3** + **xunit.runner.visualstudio 3.1.4** —
  Apache License 2.0 / MIT (test project only).
- **Microsoft.NET.Test.Sdk 17.14.1** — MIT License (test only).
- **coverlet.collector 6.0.4** — MIT License (test only).

## Services used at runtime (not code, not bundled)

- **PayPal Sandbox** (Orders API + webhooks) — PayPal's developer
  terms. Sandbox only: fictitious accounts, no real money.
- **Elastic** — the app ships a config-selectable search provider
  that speaks to an Elastic node over HTTP; the built-in search is
  the shipped default and no Elastic code is bundled. The live
  Elastic node was not run for this build; see docs/SPONSORS.md.
