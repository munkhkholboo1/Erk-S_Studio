# Sheet Package Contract

Status: normative connector contract

Current schema: `4`

## Purpose

A sheet package is the lossless hand-off between an authoring application and Erk-S Studio.
It contains vector PDF output and metadata only. It must never contain an RVT, DWG, credential,
license, token, or other professional native source file.

## Package layout

```text
<delivery>/
|-- <sheet-1>.pdf
|-- <sheet-2>.pdf
`-- <export-name>.erks-sheets.json
```

The producer MUST finish and close every PDF before publishing the manifest. The manifest MUST
be written through a temporary file and atomically renamed into place. Its directory is the
package root.

## Manifest fields

`SheetPackageManifest` contains:

| Field | Rule |
| --- | --- |
| `schemaVersion` | MUST be `4` for current producers. Studio reads versions 1-3 only for compatibility. |
| `packageId` | Non-empty UUID unique to one export run and used for idempotency. |
| `source.sourceId` | Stable Studio source-registry ID; required in v4. |
| `source.application` | `Revit`, `AutoCad`, `CityGen`, `Manual`, or `Pdf`. |
| `source.applicationVersion` | Human-readable producer version. |
| `source.documentPath` | Local binding metadata only; never authorizes upload. |
| `source.documentTitle` | Display name of the source. |
| `source.projectCode` | Project grouping hint. |
| `projectId`, `stageId`, `workPackageId` | Optional canonical assignment metadata. |
| `packageScope` | `Delta` or `FullSnapshot`. |
| `exportedAtUtc` | UTC timestamp of the completed export. |
| `sheets` | Ordered list of package entries. |

Every sheet entry MUST have a stable `sheetId`, relative `.pdf` filename, positive page count,
lower-case SHA-256, and positive finite `widthMm`/`heightMm`. Current producers also provide
page-format identity and inline geometry.

## Package scope

- `Delta` updates only sheets present in the package and never implies deletion.
- `FullSnapshot` is the complete current set for exactly one `sourceId`. A newer valid snapshot
  may remove only omitted sheets belonging to that source.
- An empty package is valid only as a `FullSnapshot`; it means that source currently has no sheets.
- An invalid or older snapshot MUST NOT remove or replace verified records.

## Page format geometry

`PageFormatSpec` uses millimetres from the physical page top-left. It defines:

- physical width and height;
- drawing area;
- sheet-title and title-block areas;
- mode, code, orientation, and binding edge;
- border/grid flags and module metadata;
- revision and SHA-256 `geometryHash`.

The format is parametric page geometry, not CAD/BIM geometry. The same contract is shared by
Revit, AutoCAD, CityGen, and Studio.

For `isCleanDrawingSpace=true`:

- an inline format is required;
- `contentWidthMm` and `contentHeightMm` MUST equal the format drawing area;
- the source PDF physical page MUST match the declared clean content size;
- official border, grid, sheet title, corner table, company data, and project data MUST be absent.

## Path and file security

Only package-contained relative PDF paths are allowed. Studio rejects:

- Windows, Unix, UNC, URI, or drive-rooted paths;
- `.` or `..` traversal segments;
- null characters and invalid normalized paths;
- resolved paths outside the manifest directory;
- symlinks, junctions, and reparse-point escapes;
- duplicate sheet IDs, filenames, or resolved paths;
- non-PDF extensions, missing files, hash mismatches, or page-count mismatches.

All paths are normalized with `Path.GetFullPath`, checked with `Path.GetRelativePath`, and
reparse-point validated before a verified path is exposed to a consumer.

## Acceptance semantics

Acceptance is fail closed and package atomic:

1. Parse and validate the manifest header.
2. Validate every sheet and page-format geometry.
3. Resolve every path inside the package root.
4. Verify every PDF SHA-256, page count, and physical dimensions.
5. Accept all records only after the whole package passes.

Rejected packages remain in place and are recorded in
`.erks-quarantine/rejected-packages.jsonl`. Rejection does not update the sheet library,
album pages, source timestamp/status, deletion reconciliation, or cloud-sync metadata.

## Canonical validation command

```powershell
dotnet run --project src\tools\ErkS.PackageAcceptance\ErkS.PackageAcceptance.csproj `
  -c Release -- <manifest.erks-sheets.json>
```

Connector release acceptance MUST use this validator, not a connector-specific relaxed parser.

## Compatibility and change policy

- Adding optional fields is backward compatible within schema v4.
- Changing field meaning, trust semantics, hash semantics, coordinate origin, or deletion behavior
  requires a new schema version.
- A schema change starts with reader/writer/host acceptance tests and a migration note.
- Studio MUST never guess an unsupported schema or continue with partially verified content.
