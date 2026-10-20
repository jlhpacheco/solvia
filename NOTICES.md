# NOTICES — third-party licenses

This inventory covers application and test package references, vendored
libraries and fonts, CDN components, and optional external model/runtime
services called by the current code. An external service is not bundled
with Solvia and is governed by its own terms; listing an integration does
not claim that its live deployment has been verified.

Solvia itself is MIT-licensed (see LICENSE at the repo root).
Copyright (c) 2026 Jose Luis Pacheco.

## Components the app ships or calls

- **PDFsharp 6.2.3** — MIT License. Generates the student/employee
  permit PDFs in `PermitPdfService`. [Upstream license](https://github.com/empira/PDFsharp/blob/master/LICENSE).
- **Roboto Regular and Bold** — Apache License 2.0. Embedded fonts
  under `src/Solvia/Assets/Fonts/`; their complete license and pinned
  upstream provenance are retained there. These assets come from the
  Apache-licensed Roboto 2 distribution, not a different font release.

- **Microsoft.EntityFrameworkCore.Sqlite 10.0.13** — MIT License.
  The app's only database provider (EF Core + SQLite, a single
  local file; there is no database server). SQLite itself is
  public domain.
- **System.Security.Cryptography.Pkcs 10.0.13** — MIT License.
  Explicitly overrides PDFsharp's older transitive dependency with the
  patched version identified by Microsoft's October 2026 advisories.

- **Microsoft Phi-4-mini** — MIT License. The AI model. It is NOT
  bundled with this repo: the default endpoint runs on the app host,
  served by Ollama (`ollama pull phi4-mini`, about 2.5 GB).
  Microsoft is the model's author; the model card and license ship
  with the model, not with Solvia.

- **Ollama** — MIT License. External runtime, NOT bundled. This is the
  default AI adapter (`AI_PROVIDER=ollama`), with endpoint
  `http://localhost:11434` local to the application host by default. Its
  endpoint is configurable. An unavailable runtime or unusable model
  answer uses the built-in deterministic reader.

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
- **PdfPig 0.1.12** — Apache License 2.0 (test-only PDF text extraction).
- **Microsoft.AspNetCore.Mvc.Testing 10.0.13** — MIT License (test-only application host).
- **Playwright Test 1.64.1** and **TypeScript 7.0.2** — Apache License 2.0 (browser tests only).
- **@types/node** — MIT License (browser test tooling only).

## Services used at runtime (not code, not bundled)

- **PayPal Sandbox** (Orders API + webhooks) — PayPal's developer
  terms. Sandbox only: fictitious accounts, no real money.
- **Meta Model API — Standard Muse Spark** — optional proprietary hosted
  service, not a bundled model or MIT-licensed component. The default
  configured Standard model is `muse-spark-1.3`, selected only with
  `AI_PROVIDER=meta` and a server-side API key. See the official
  [API overview](https://dev.meta.ai/docs/overview) and
  [Meta Model API Terms](https://dev.meta.ai/legal/terms-of-service).
  This demo uses fictional adults (18+) when this provider is selected;
  see [privacy](docs/PRIVACY.md) for age, retention, training, and credential
  boundaries. No live Meta request has been verified.
- **Elastic** — the app ships a config-selectable search provider
  that speaks to an Elastic node over HTTP; the built-in search is
  the shipped default and no Elastic code is bundled. The live
  Elastic node was not run for this build; see docs/SPONSORS.md.
