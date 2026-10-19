# NOTICES — third-party licenses

Solvia itself is released under the MIT License (see `LICENSE`).
The third-party pieces it uses are listed below, each with its
license and how it reaches the user.

## AI model and runner

- **Microsoft Phi-4-mini** — MIT License (Microsoft). The AI model.
  It is not bundled in this repo: it is downloaded separately
  (`ollama pull phi4-mini`) and runs 100% locally through Ollama, so
  no student data leaves the machine.
- **Ollama** — MIT License. An external tool, NOT bundled with this
  app: when Ollama is installed, the app calls its local HTTP API to
  run Phi-4-mini. Ollama is optional — the app runs fully without it
  through the built-in fallback reader (see `docs/SPONSORS.md`).

## App dependencies (NuGet)

- **Microsoft.EntityFrameworkCore.Sqlite 10.0.13** — MIT License.
  A NuGet package (`src/Solvia/Solvia.csproj`); the database layer
  over SQLite.
- **QuestPDF 2026.2.4** — QuestPDF Community License. This is a
  source-available commercial license — it is **NOT** MIT, NOT
  Apache, and NOT an OSI-approved open-source license, and this
  entry does not describe it as one. It is free for this project:
  the Community License (v3.0, effective 2026-07-06) covers,
  among others, open-source projects distributed under an
  OSI-approved license (Solvia itself is MIT-licensed), individual
  use, and learning and evaluation. Note the limit, stated
  plainly: **public-sector entities and publicly traded companies
  are NOT eligible for the Community License, regardless of
  revenue** — an operator in one of those categories would need a
  paid QuestPDF license. Terms:
  https://www.questpdf.com/license/ (Community License +
  License Selection Guide). QuestPDF renders the printable
  permit PDF at runtime (`Services/PermitPdfService.cs`).

## Web libraries shipped in `src/Solvia/wwwroot/lib/`

Each library's own license file ships in-tree beside it.

- **Bootstrap 5.3.3** — The MIT License (MIT)
  (`wwwroot/lib/bootstrap/LICENSE`). CSS/JS framework for the pages.
- **jQuery 3.7.1** — MIT License (`wwwroot/lib/jquery/LICENSE.txt`,
  Copyright OpenJS Foundation and other contributors).
- **jQuery Validation 1.21.0** — The MIT License (MIT)
  (`wwwroot/lib/jquery-validation/LICENSE.md`). Form validation.
- **jQuery Validation Unobtrusive** — The MIT License (MIT)
  (`wwwroot/lib/jquery-validation-unobtrusive/LICENSE.txt`,
  Copyright .NET Foundation and Contributors). Bridges jQuery
  Validation to the app's server-side validation attributes.

## Loaded at runtime from a CDN (not bundled)

- **AG Grid Community 34.3.1** — MIT License (the `license` field of
  the `ag-grid-community@34.3.1` package metadata). Loaded from the
  official jsDelivr CDN in `src/Solvia/Views/Shared/_FeeTable.cshtml`
  to render the 10-college fee table. If the CDN is unreachable, the
  fee table falls back to a built-in native renderer with the same
  data.

## Test-only dependencies (never shipped in the app)

Referenced only by `tests/Solvia.Tests/Solvia.Tests.csproj`:

- **xUnit 2.9.3** — Apache License 2.0.
- **xunit.runner.visualstudio 3.1.4** — Apache License 2.0.
- **Microsoft.NET.Test.Sdk 17.14.1** — MIT License.
- **coverlet.collector 6.0.4** — MIT License.

(The test project also references Microsoft.EntityFrameworkCore
.Sqlite 10.0.13, listed above.)

## Evaluated and NOT used

- **Bryntum Calendar** was evaluated in track T5 and NOT used (no
  free edition; its 45-day trial would expire during judging) — no
  Bryntum code ships, so no Bryntum license applies. See
  docs/SPONSORS.md.
