# Sheet Package Security and Vector PDF Pipeline

This document defines the Studio trust boundary for Revit, AutoCAD, CityGen,
and manual PDF deliveries. It supplements `ARCHITECTURE.md` and applies to
sheet package schema 4. Schema 1-3 remains readable for compatibility.

## Trust boundary

A `*.erks-sheets.json` manifest and every path inside it are untrusted input.
Studio accepts a package only when the whole package passes all checks:

- supported schema and non-empty package/source/sheet identifiers;
- unique sheet identifiers and PDF filenames;
- package-contained relative PDF paths only;
- no absolute path, URI, UNC path, traversal, symlink, or reparse-point escape;
- every PDF exists and its SHA-256 matches;
- declared page count matches the PDF;
- schema 4 physical dimensions match the first PDF page;
- inline format geometry and geometry hash are valid;
- clean drawing-space dimensions match the format drawing area.

Validation is package-atomic. One issue rejects the complete package. An
invalid full snapshot cannot delete sheets and an invalid delta cannot replace
a previously verified sheet.

## Rejected packages

Rejected files are not moved or deleted. The watched source folder receives an
append-only audit log at:

```text
.erks-quarantine/rejected-packages.jsonl
```

Each record contains the manifest path, package id, UTC rejection time, and
validation issues. Studio reports the state as `Rejected package`. Rejection
does not update album pages, source `LastPackageAtUtc`, source status, or cloud
sync metadata.

## Producer rule

`SheetPackageWriter` uses the same validation boundary as intake. It computes
PDF hashes, writes a temporary manifest beside the final file, re-reads and
validates the complete package, then atomically moves the manifest into place.
An invalid producer result never replaces the previous published manifest.

## Verified-only album build

`SheetLibrary` stores only package-contained verified paths. `AlbumBuilder`
resolves only verified records and re-validates every referenced package before
composition. This closes the time-of-check/time-of-use gap after intake.

Album output is also atomic:

1. validate all source packages;
2. compose to a unique temporary PDF in the output directory;
3. move it over the canonical album only after successful composition;
4. keep the previous canonical PDF unchanged on any failure.

Missing, changed, or unverified source content causes `AlbumBuildException`.
The writer does not silently skip a failed source page.

## Vector composition

`SourceAsIs` imports every source page directly. It does not render to a bitmap,
resize the page, or change page order. Mixed page sizes are retained.

`PreserveDrawingSpace` imports the source as a PDF Form XObject. The source
drawing-space dimensions must equal the target drawing area within 0.75 mm.
The placement matrix uses 1:1 scale; only translation is allowed. A mismatch
stops the build instead of stretching or shrinking the drawing.

## Golden test framework

`PdfVectorQualityInspector` provides renderer-independent structural profiles:

- physical page, MediaBox, and effective CropBox dimensions;
- ordered PDF content operators and content-stream SHA-256;
- text and path-painting operator presence;
- Image and Form XObject counts;
- imported Form XObject bounding boxes and placement matrices.

The P0 test suite verifies A3 landscape, A4 portrait, custom mixed sizes,
multi-page order, text/path operators, no full-page image fallback, Form XObject
preservation, and 1:1 clean drawing-space placement. Exporter-specific visual
golden renders are added with each Revit and AutoCAD reference export.

## Verification commands

```powershell
dotnet build src\src\ErkS.Studio.App\ErkS.Studio.App.csproj -c Release
dotnet test src\tests\ErkS.Platform.Core.Tests\ErkS.Platform.Core.Tests.csproj -c Release
dotnet test src\tests\ErkS.Platform.Core.Tests\ErkS.Platform.Core.Tests.csproj `
  -c Release --collect:"XPlat Code Coverage"
```

Security-sensitive changes require a failing regression test first. Do not
weaken validation or update a golden reference merely to make CI pass.

## Cloud state protection

Cloud project mutations use `ETag`/`If-Match` optimistic concurrency. A missing
or stale token is rejected; Studio preserves pending local edits and never marks
a conflict as synchronized. Source package and album-revision operations use
stable idempotency identities so timeout/retry cannot create duplicates.

After sign-in, Studio negotiates the versioned capabilities endpoint and requires
the features used by the workflow. It does not infer support from a 404 response.
The generated OpenAPI client and server snapshot are verified deterministically
in CI.

## Update trust

Product update transport is HTTPS only. A development build may use loopback
HTTP, but never arbitrary HTTP. Before launch, Studio verifies SHA-256, Windows
PE format, Authenticode trust/chain/revocation, and exact signer publisher. It
repeats the verification immediately before executing the installer.

The production publisher is `Erk-S LLC`. Unsigned, modified, wrong-publisher,
non-executable, or hash-mismatched files fail closed. See `UPDATE-SIGNING.md`.

## Data and relationship boundary

RVT, DWG, credentials, tokens, license material, and local project data are
forbidden from release artifacts and source history. Native files remain local;
only verified documents, manifests, hashes, and source metadata cross the cloud
boundary. Membership, organization, grant, exit, and source-custody operations
require the current relationship-policy acknowledgement and server audit record.

See `RELATIONSHIP-BOUNDARY.md` and `SOURCE-FILE-CUSTODY.md`.

## CI and release gates

Studio CI performs Release build, full tests with coverage, focused security
regression tests, structural vector-PDF golden tests, generated-client drift
verification, WPF product publish smoke, payload scanning, and signing-gate
syntax verification. A scheduled workflow runs scale regressions for package
intake, mixed-format album build, preview cache, and large-source hashing.

The product release script additionally requires a real production certificate,
signs the final executable and setup, validates publisher/signature, performs an
isolated smoke install, and scans the installed payload. Branch protection must
make the CI check required in GitHub; that repository setting is an operational
release prerequisite.
