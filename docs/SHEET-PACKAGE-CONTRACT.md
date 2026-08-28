# Sheet Package Contract

Status: normative connector contract

Current schema: `5`

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
| `schemaVersion` | MUST be `5` when a producer uses per-sheet PDF page references. Existing schema-4 producers remain current and valid; Studio reads versions 1-3 for legacy compatibility. |
| `packageId` | Non-empty UUID unique to one export run and used for idempotency. |
| `source.sourceId` | Stable Studio source-registry ID; required in v4. |
| `source.application` | `Revit`, `AutoCad`, `CityGen`, `Manual`, or `Pdf`. |
| `source.applicationVersion` | Human-readable producer version. |
| `source.documentPath` | Local binding metadata only; never authorizes upload. |
| `source.documentTitle` | Display name of the source. |
| `source.projectCode` | Project grouping hint. |
| `projectId`, `stageId`, `workPackageId` | Optional canonical assignment metadata. `projectId` is matched against the receiving project. `stageId` and `workPackageId` are OPAQUE optional strings: producers have written both Guid ("N") values and stage codes, Studio's UI never fills them, and no consumer may parse or rely on their format. |
| `packageScope` | `Delta` or `FullSnapshot`. |
| `exportedAtUtc` | UTC timestamp of the completed export. |
| `exportMode` | Producer-informational free text (e.g. `"SheetsAsIs"`, `"LayoutsAsIs"`). Studio records it and never reads it; nothing may depend on its value. |
| `sheets` | Ordered list of package entries. |

Every sheet entry MUST have a stable `sheetId`, relative `.pdf` filename, positive page count,
lower-case SHA-256, and positive finite `widthMm`/`heightMm`. Current producers also provide
the one-based `pdfPageNumber`, page-format identity, inline geometry, and `printColorMode`.

## Per-sheet print color

`printColorMode` is exactly one of these JSON strings:

- `Original` — preserve the drawing's authored colors;
- `BlackAndWhite` — producer-exported black-and-white output;
- `Grayscale` — producer-exported grayscale output.

The producing application applies the selected treatment while exporting each logical sheet.
The vector PDF bytes are authoritative. Studio records the mode for audit but MUST NOT recolor,
rasterize, or infer a different mode from the PDF. Manifests written before this optional field
was introduced deserialize as `Original`.

Schema 5 permits several logical sheet entries to reference distinct `pdfPageNumber` values in
one multi-page PDF. Consequently every page can retain its own `printColorMode` even when the
entries share the same PDF file and SHA-256.

## Per-sheet destination

`destination` says where the receiving side files an entry. It is exactly one of these JSON
strings:

- `Album` — the entry is bound into the project album (the behavior of every package written
  before this field existed; missing values deserialize as `Album`);
- `Portfolio` — the entry is a presentation page for the project portfolio. It MUST NOT enter
  the sheet library, the album, or any album composition.

Any other value fails package verification, the same way an unknown `printColorMode` does.

A producer writing a `Portfolio` entry MUST export it with `format.mode = "Portfolio"`, an
equal 10 mm margin on every edge, and zero-size `sheetTitleArea` and `titleBlockArea`: the page
carries no corner table, no sheet title strip and no reference grid. Paper sizes stay the
standard `A1`–`DH4` matrix. Hashing, page references, print color, and package scope are
unchanged by this field.

Studio imports each `Portfolio` entry as one portfolio item: the PDF is copied into the
project's own storage and the item is keyed by source identity plus `sheetId`, so a re-export
updates the same item (its content, title, and page reference) while the user's ordering,
caption, layout, and focal point stay untouched. The page is placed full-bleed because it
already carries its own margin, and an imported page of a different size is fitted whole,
never cropped.

## Producer-optional fields with no reader yet

A producer may write these. Studio parses none of them today, and
`System.Text.Json` drops what it does not recognise without a word, so they are
listed here to keep that silence out of the contract. Nothing may depend on
Studio acting on them.

- `viewports` — per-sheet array written by the AutoCAD producer. Reserved for
  the competition-board composer: a board draws a scale bar and a north arrow
  beside a plan, and neither can be recovered from the PDF. Studio's board
  composer exists (`BoardScaleBar`, `BoardPlanImportService`) but is fed from
  the CityGen board channel, not from sheet packages. Whether AutoCAD sheets
  should also become board plans is an open product decision.

  Two things must be settled before that channel could be joined, recorded here
  so they are not rediscovered:

  - The "north is known" flag means different norths on the two sides. The
    sheet package writes `northSource: geographic | assumed`; the CityGen board
    manifest writes `NorthAngleSource: utm-grid | assumed`. UTM grid north and
    true north differ by the meridian convergence - about 2.2° at the edge of a
    zone at Mongolian latitudes, under 1° near its central meridian. Small, and
    a trap precisely because it is small: two flags that read the same word mean
    two different directions.
  - The sheet package carries no north angle at all. Its `twistAngleDegrees` is
    the viewport's own rotation, which is not the angle a north arrow needs;
    deriving one from the other requires the model's north, which is not sent.

- `levelId`, `levelName` — written by producers, read by nothing.
- `drawingAssetId`, `drawingAssetVersion`, `drawingAssetSha256` — no producer
  populates them (AutoCAD and Revit both send empty strings, because neither
  delivers a vector source file). `drawingAssetSha256` is described elsewhere as
  the hash a released album page pins; no code implements that.

## Package scope

- `Delta` updates only sheets present in the package and never implies deletion.
- `FullSnapshot` is the complete current set for exactly one `sourceId`. A newer valid snapshot
  may remove only omitted sheets belonging to that source.
- An empty package is valid only as a `FullSnapshot`; it means that source currently has no sheets.
- An invalid or older snapshot MUST NOT remove or replace verified records.
- `Album` and `Portfolio` entries are two independent sets inside one package. A snapshot's
  omissions delete only within the same destination: portfolio entries never look like
  deletions of album sheets, and album entries never look like deletions of portfolio pages.
  A snapshot that omits a previously delivered portfolio page does not remove the imported
  portfolio item; the portfolio is the receiving project's own presentation and must not
  lose content.

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

## Studio-generated album pages

Studio composes some album pages itself; a producer MUST NOT deliver sheets
that stand in for them (block them from export the way PFR's workflow boundary
does). The authoritative slots and their exact titles (Ordinal):

Building-architecture concept album
(`BuildingArchitectureConceptAlbumTemplate.cs`):

| Slot id | № | Title | Section |
| --- | --- | --- | --- |
| `cover` | 00 | `НҮҮР ХУУДАС` | Нүүр хуудас |
| `design-organization` | 01 | `ЗУРАГ ТӨСӨЛ БОЛОВСРУУЛСАН БАЙГУУЛЛАГА` | Ерөнхий хэсэг |
| `planning-task` | 02 | `БАТЛАГДСАН АРХИТЕКТУР ТӨЛӨВЛӨЛТИЙН ДААЛГАВАР` | Ерөнхий хэсэг |
| `site-context` | 03 | `БАЙРШЛЫН СХЕМ / ОРЧНЫ ТОЙМ` | Ерөнхий төлөвлөгөө |

Building working-drawing album (`BuildingWorkingDrawingAlbumTemplate.cs`):

| Slot id | № | Title | Section |
| --- | --- | --- | --- |
| `cover` | 00 | `НҮҮР ХУУДАС` | Ажлын зураг |
| `drawing-list-and-notes` | 01 | `ЗУРГИЙН ЖАГСААЛТ, ТАЙЛБАР БИЧИГ` | Ажлын зураг |

Both are composed by Studio, which draws the working-drawing cover through its
own page format (`PdfSharpAlbumWriter`, `UsesGeneratedWorkingDrawingFormat`).
Producers MUST NOT deliver either.

> **Corrected 2026-08-29.** This paragraph previously said these two remained
> producer-owned on the Revit side, and asked producers to update their block
> lists on the day that changed. The day had already passed: Studio had taken
> both slots, and nobody told the producers - the notification this paragraph
> asked for, never sent, in the direction it did not anticipate.
>
> It was found by a test written to fire on that future day, which failed on its
> first run. The rule was left standing and flagged rather than rewritten in
> place, until PFR confirmed the effect from their side, because this paragraph
> is what their export boundary follows.
>
> The effect was real and is one page, not two. PFR's default working-drawing
> album template creates `НҮҮР ХУУДАС` and its export boundary passes it, so an
> album carried two covers: the one Revit sent and the one Studio drew. PFR does
> not generate a drawing-list sheet, so that slot was only ever at risk if
> someone named a sheet by hand.

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

- Adding optional fields is backward compatible within schema v4 or v5 when their defaults preserve prior behavior.
- Changing field meaning, trust semantics, hash semantics, coordinate origin, or deletion behavior
  requires a new schema version.
- A schema change starts with reader/writer/host acceptance tests and a migration note.
- Studio MUST never guess an unsupported schema or continue with partially verified content.
