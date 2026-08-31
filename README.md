# Logic App Storage Inspector

Web-based diagnostics tool for **Azure Logic Apps Standard** that provides visibility into
flow execution history, workflow version management, and storage/queue health — scoped to a
single site even when many sites share the same storage account.

It is the v2 successor to `LogicAppActionScanner` (console) and
`LogicAppActionScannerExtension` (site extension). It reuses the proven storage-decode engine
(ZStandard + variable-length-integer framing + `ContentLink` / `nestedContentLinks` unwrapping)
and adds a full Fluent UI front end.

> Status: specification + scaffold. See [Roadmap](#roadmap) for what is implemented.

---

## Table of contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Site scoping (multi-site safety)](#site-scoping-multi-site-safety)
- [Pages](#pages)
  - [Page 1 - Flow History Search](#page-1---flow-history-search)
  - [Page 2 - Flow Versions](#page-2---flow-versions)
  - [Page 3 - Information Dashboard](#page-3---information-dashboard)
- [Storage model](#storage-model)
- [Configuration](#configuration)
- [Security](#security)
- [Performance](#performance)
- [Build and run](#build-and-run)
- [Deploy as a site extension](#deploy-as-a-site-extension)
- [Project structure](#project-structure)
- [Roadmap](#roadmap)

---

## Overview

| Capability | Summary |
|---|---|
| Flow history search | Async, cancellable full-text search over action/trigger inputs and outputs |
| Flow versions | Tree of flows and versions, read-only viewer, VS Code-style diff |
| Info dashboard | Table row counts, queue depths and health, auto-refresh |
| Multi-site safe | Every query is filtered to the current site via `WEBSITE_SITE_NAME` |

The tool runs **inside the target Logic App Standard site** (as a Kudu site extension), so it reads
the same `AzureWebJobsStorage` and the same site identity the runtime uses.

---

## Architecture

```
+---------------------------------------------------------------+
|  Browser (Fluent UI v9, dark theme)                           |
|                                                               |
|  Page 1 History  |  Page 2 Versions  |  Page 3 Dashboard      |
|  - DataGrid      |  - TreeView       |  - Cards / DataGrid     |
|  - filters       |  - Monaco viewer  |  - auto refresh         |
|  - CSV/JSON      |  - Monaco Diff    |                         |
+-----------------------------|---------------------------------+
                              |  fetch (JSON, AbortController)
+-----------------------------v---------------------------------+
|  ASP.NET Core minimal API (net9.0)                            |
|                                                               |
|  /api/flows            list flows for current site            |
|  /api/history/search   async, cancellable, paged              |
|  /api/versions         flow -> version tree                   |
|  /api/versions/content single version content                 |
|  /api/versions/diff    two-version diff payload               |
|  /api/dashboard/tables table stats                            |
|  /api/dashboard/queues queue stats                            |
|                                                               |
|  SiteScope  -> resolves current-site table/queue prefix       |
|  ScanEngine -> decode (ZStandard + VLI + ContentLink)         |
|  Azure.Data.Tables / Azure.Storage.Queues (server-side filter)|
+-----------------------------|---------------------------------+
                              |  Managed identity (preferred)
                              |  or AzureWebJobsStorage
+-----------------------------v---------------------------------+
|  Azure Storage (shared by many sites)                         |
|  Tables: flow<hostId>...actions / runs / histories / flows    |
|  Queues: flow<hostId>-... job queues                          |
+---------------------------------------------------------------+
```

**Front end:** React + Fluent UI v9 (`@fluentui/react-components`), dark theme by default,
Monaco Editor for the read-only viewer and the diff view.

**Back end:** ASP.NET Core minimal API, self-contained publish so it runs in Kudu without a
pre-installed runtime. All endpoints are `async` and honour `CancellationToken`.

---

## Site scoping (multi-site safety)

A single storage account can host **many** Logic App Standard sites. Their tables are
distinguished by a per-site prefix, e.g. observed groups such as `flow69b2a0447ba1f52...`,
`flow529a8eeb8b2e1b7...`, `flowd07f7132714c79a...`. Showing rows from another site would be a
data-leak, so scoping is a hard requirement (spec 2.9, 2.10, 7.1, 7.2).

**Strategy**

1. Read the current site name from `WEBSITE_SITE_NAME`.
2. Resolve the **current site table/queue prefix** (the workflow runtime derives a stable
   `flow<hostId>` prefix per site). The inspector computes/resolves the same prefix and caches it.
3. Enumerate **only** tables and queues whose name starts with that prefix; all reads are
   filtered server-side.
4. Never enumerate or return rows from any other prefix.

> Implementation note: the resolver mirrors the runtime's UTF-8, seed-zero `MurmurHash64` host-ID
> calculation and 15-character storage-prefix truncation. It reads
> `Microsoft.Azure.Workflows.HostId` when configured and otherwise uses the lowercased App Service
> site name. `LASI_TABLE_PREFIX` remains a validated explicit override.

---

## Pages

### Page 1 - Flow History Search

- Flow selector: single, multiple, or **All Flows** (multiselect).
- Date range selector.
- Action/trigger content search box (e.g. `search21`) - **partial**, **case-insensitive**.
- Search is **async** and **cancelled** when a new search is submitted (AbortController + server
  `CancellationToken`).
- Results columns: Flow Name, Run ID, Action Name, Timestamp, Action Content.
- Sortable and column-filterable grid, paginated.
- Copy a single row, copy multiple selected rows.
- Export to **CSV** and **JSON**, for **all filtered** results or **selected rows only**.

### Page 2 - Flow Versions

- Hierarchical **tree**: flow -> versions, expand/collapse.
- Each version shows identifier, creation date, and author when available.
- Read-only version viewer with JSON **syntax highlighting** and in-document search (Monaco).
- Select **any two** versions to compare.
- Diff resembles the **VS Code diff**: additions green, removals red, modifications highlighted,
  with **side-by-side** and **inline** modes.
- Copy and export comparison results.

### Page 3 - Information Dashboard

- Storage: total table count, table names, row counts, last-modified when available.
- Queues: Logic App queues, message counts, status and health indicators, auto-refresh.
- Manual refresh button, automatic refresh, last-refresh timestamp, loading indicators.

---

## Storage model

Logic Apps Standard persists workflow state in **Azure Storage Tables** (and job **Queues**),
prefixed per site.

| Table suffix | Contents |
|---|---|
| `...actions` (dated) | Per-run action records: `ActionName`, `Status`, `FlowName`, `FlowRunSequenceId`, `InputsLinkCompressed`, `OutputsLinkCompressed`, timestamps |
| `...runs` | Run summaries |
| `...histories` | Trigger histories |
| `...flows` | Flow definitions / versions |
| `...variables` (dated) | Workflow variables |

**Content encoding.** `InputsLinkCompressed` / `OutputsLinkCompressed` are binary:

1. A **variable-length-integer (LEB128)** header whose low 3 bits are the algorithm marker
   (`7` = ZStandard, `6` = LZ4, otherwise raw Deflate).
2. The compressed frame decompresses to a `ContentLink` JSON.
3. `inlinedContent` is base64 of the real payload; large payloads use `uri` (blob) or
   `nestedContentLinks` (`root` / `body`).

This decode path is inherited from `LogicAppActionScanner` and lives in `ScanEngine`.

---

## Configuration

All configuration comes from environment variables (spec 2.7, 7.4). Nothing is hardcoded.

| Variable | Purpose |
|---|---|
| `WEBSITE_SITE_NAME` | Current site name - drives all site scoping |
| `AzureWebJobsStorage` | Storage connection (fallback when managed identity is unavailable) |
| `AzureWebJobsStorage__accountName` | Account name for managed-identity access |
| `LASI_AUTO_REFRESH_SECONDS` | Dashboard auto-refresh interval (optional) |
| `LASI_PAGE_SIZE` | Default result page size (optional) |

---

## Security

- Users only ever see data for the current site; filtering is enforced **server-side** (7.1, 7.2).
- Storage access uses **managed identity** when available, connection string only as fallback (7.3).
- No configuration values are hardcoded (7.4).
- All user actions (searches, exports, version views/compares) are **logged for auditing** (7.5).
- Exports honour the same site-level filtering as the UI (7.6).
- The extension is reachable only through the **SCM (Kudu)** site, so access requires Kudu
  publishing credentials.

---

## Performance

- All search and storage operations are **async** and accept **cancellation tokens** (6.1, 6.6).
- Results load **progressively** / paged; entire tables are never loaded into memory (6.2, 6.3).
- Queries use **server-side filtering** (partition/row-key and property filters) wherever possible (6.4).
- Designed for large accounts with **millions of records** (6.5).

---

## Build and run

Prerequisites: .NET SDK 9, Node.js 20+ (for the Fluent UI front end).

```powershell
# back end
dotnet build -c Release

# front end (from the web client folder)
npm install
npm run build      # emits static assets served by the API

# run locally
$env:WEBSITE_SITE_NAME = "my-logicapp"
$env:AzureWebJobsStorage = "<connection-string>"   # local dev only
dotnet run -c Release
```

Then browse `http://localhost:5xxx`.

---

## Deploy as a site extension

Packaged the same way as `LogicAppActionScannerExtension`:

1. `dotnet publish -c Release -r win-x64 --self-contained true -o publish`
2. Include `scmApplicationHost.xdt` (mounts under the SCM site) and the ANCM out-of-process `web.config`.
3. Pack with a nuspec that sets:
   ```xml
   <packageTypes>
     <packageType name="AzureSiteExtension" />
   </packageTypes>
   ```
   (required for the Kudu Site Extensions Gallery to list it) and `target="content"`.
4. Upload to nuget.org (gallery filters by `AzureSiteExtension`) or push to a private feed and set
   `SCM_SITEEXTENSIONS_FEED_URL` on the target app.

---

## Project structure

```
LogicAppStorageInspector/
  README.md
  LogicAppStorageInspector.csproj      backend (ASP.NET Core, net9.0)
  Program.cs                           API endpoints + static hosting
  Services/
    SiteScope.cs                       current-site prefix resolver
    ScanEngine.cs                      ZStandard/VLI/ContentLink decode
    HistorySearchService.cs            async, cancellable, paged search
    VersionService.cs                  flow/version tree + content + diff
    DashboardService.cs                table + queue stats
  web/                                 React + Fluent UI v9 + Monaco
    src/pages/HistorySearch.tsx
    src/pages/FlowVersions.tsx
    src/pages/Dashboard.tsx
  packaging/
    scmApplicationHost.xdt
    web.config
    LogicAppStorageInspector.nuspec
```

---

## Roadmap

- [ ] Backend scaffold + `SiteScope` prefix resolver with tests
- [ ] Port `ScanEngine` decode from v1
- [ ] Page 1 history search API + Fluent UI grid, copy/export
- [ ] Page 2 version tree + Monaco viewer + diff
- [ ] Page 3 dashboard (tables + queues) with auto-refresh
- [ ] Managed-identity storage access
- [ ] Audit logging
- [ ] Site-extension packaging + gallery publish