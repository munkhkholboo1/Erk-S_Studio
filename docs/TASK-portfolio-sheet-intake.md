# Task: take portfolio pages from a sheet package into the project portfolio

Status: implemented (2026-08-23); joint real-package test with PFA pending
Producer side: done (Erk-S Platform for AutoCAD, `PFA`)
Consumer side: done (Erk-S Studio)

## Why

A portfolio page is a drawing the project wants to *present*, not to bind into
the album: in the portfolio the graphic is reworked - a greenery hatch replaced
by a real grass pattern, and so on. PFA can now author such pages and already
sends them; Studio still files every incoming sheet into the album.

## What PFA already sends

The Sheet List keeps a `Портфолио` group at the bottom of every drawing stage
and package. A page added there is exported like any other sheet - one vector
PDF plus its manifest entry - but it is marked for the portfolio and its page
geometry has no album chrome:

```json
{
  "sheetId": "5F3A",
  "destination": "Portfolio",
  "number": "00",
  "name": "ПОРТФОЛИО ХУУДАС",
  "widthMm": 420,
  "heightMm": 297,
  "pdfFileName": "00-portfolio-huudas.pdf",
  "sha256": "…",
  "pdfPageNumber": 1,
  "pageCount": 1,
  "format": {
    "mode": "Portfolio",
    "code": "A1",
    "orientation": "LANDSCAPE",
    "bindEdge": "NONE",
    "widthMm": 420,
    "heightMm": 297,
    "drawingArea": { "x": 10, "y": 10, "width": 400, "height": 277 },
    "sheetTitleArea": { "x": 0, "y": 0, "width": 0, "height": 0 },
    "titleBlockArea": { "x": 0, "y": 0, "width": 0, "height": 0 },
    "showGrid": false
  }
}
```

Rules the producer guarantees:

- `destination` is `"Album"` or `"Portfolio"`. It is `"Album"` in every package
  written before this field existed, and in every package from another producer.
- A `"Portfolio"` entry always carries `format.mode == "Portfolio"`, an equal
  10 mm margin on every edge, and empty (zero-size) title-block and sheet-title
  rectangles: the page has no corner table, no sheet title strip and no
  reference grid.
- Paper sizes stay the standard `A1`–`DH4` matrix, so a portfolio page can be
  any album paper size.
- Everything else - PDF hashing, page references, print color, package scope -
  is unchanged, so the package still verifies exactly as it does today.

## What Studio has to do

1. **Contract**
   - Add `Destination` to `SheetPackageEntry`
     (`src/src/ErkS.Platform.Contracts/SheetPackage.cs`), defaulting to
     `"Album"` so older packages keep their meaning. Reject an entry that
     declares anything other than `Album` or `Portfolio` in the fail-closed
     package reader, the same way an unknown `printColorMode` is rejected.
   - Document the field in `docs/SHEET-PACKAGE-CONTRACT.md` (it is the normative
     contract; the producer already follows this text).

2. **Intake routing** (`SheetIntakeService` / `SheetLibrary`)
   - A `Portfolio` entry must never enter the sheet library and must never
     appear in the album or in any album composition.
   - A `FullSnapshot` package must keep deleting album sheets that it omits,
     without letting the portfolio entries of that package look like deletions
     of album sheets, and vice versa. Treat the two destinations as two
     independent sets inside one package.
   - Verification, quarantine and the rejected-package audit stay identical for
     both destinations.

3. **Portfolio item**
   - `ProjectPortfolioItemKinds` currently knows `Image`, `Document` and
     `AlbumPage`. Add a kind for an authored CAD page (for example `CadPage`)
     and normalize it in `ProjectPortfolio.NormalizeKind`, keeping unknown
     values falling back to `Image` as they do now.
   - Import each portfolio entry as one item: the PDF copied into the project's
     own storage, `SourcePageNumber` from `pdfPageNumber`, `Title` from the
     sheet name, and a stable identity so a re-export replaces the same item
     instead of appending a duplicate (`sheetId` plus the source identity, the
     way `SheetLibrary.MakeKey` builds its album key).
   - Re-importing a changed page must update the existing item and keep its
     `Order`, `Caption`, `Layout` and focal point.

4. **Page layout**
   - A portfolio page already carries its own 10 mm margin, so it must be placed
     `FullBleed`; `Contain` would add a second margin around the first.
   - `ProjectPortfolio.PageWidthMm` / `PageHeightMm` stay the portfolio's own
     page size. When an imported page has a different size, fit it whole and do
     not crop - a cropped drawing loses content, which a portfolio must not do.

5. **Tests** (start with the failing test, per `docs/CONTRIBUTING-TDD.md`)
   - A package with one album entry and one portfolio entry: the album entry
     lands in the sheet library, the portfolio entry does not, and one portfolio
     item is created.
   - A package written before `destination` existed: every entry is an album
     sheet.
   - An entry with an unknown destination: the package is rejected and
     quarantined, and nothing is imported from it.
   - Re-import of the same portfolio page: one item, updated content, unchanged
     order and caption.
   - A full snapshot that drops one album sheet and keeps the portfolio page:
     the album sheet is removed, the portfolio item survives.

## Out of scope

- The graphic rework itself (grass pattern instead of a greenery hatch) - that
  is the portfolio editor's own feature.
- Any change to how the album is composed or printed.
- Anything on the PFA side: it is finished and its behavior is described above.

## Implementation notes (Studio, 2026-08-23)

- `Destination` lives on `SheetPackageEntry` with `SheetDestinations.Album`/`Portfolio`
  constants; the fail-closed reader rejects any other value exactly, the way an unknown
  `printColorMode` string is rejected. Documented in `docs/SHEET-PACKAGE-CONTRACT.md`.
- `SheetLibrary.Absorb` treats the two destinations as independent sets: a portfolio entry
  never becomes a library record, a full snapshot's deletions are judged within each set
  alone, and `SheetLibraryChange.NewPortfolioEntryCount` lets a portfolio-only package reach
  project reconciliation (`HasChanges` includes it).
- `PortfolioSheetImportService` (new, `ErkS.Platform.Core`) copies each verified portfolio
  PDF into project storage via `ProjectDocumentFileStore` and creates/updates one `CadPage`
  item keyed by `SourceSheetKey` (source identity + sheet id). `SourceExportedAtUtc` on the
  item keeps a re-scanned older export from rolling a newer import back. Wired into
  `AppState.RecordPackageReceived` after reconciliation.
- Decision: a full snapshot that omits a previously imported portfolio page does NOT remove
  the item. The portfolio is the project's own presentation and must not lose content; the
  user removes pages there.
- Tests: `src/tests/ErkS.Platform.Core.Tests/PortfolioSheetIntakeTests.cs` (5 tests covering
  the task's test list). Full suites green: Core 446, App 421.

## Producer reference

- `AutoCAD_v2/README.md` - "Portfolio Layouts".
- `StudioPageFormatFactory.CreatePortfolio` - the page geometry.
- `StudioSheetDestinations` - the two destination values.
