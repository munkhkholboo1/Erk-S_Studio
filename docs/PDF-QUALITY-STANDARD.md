# PDF Quality Standard

Status: normative deliverable quality gate

## Quality objective

Erk-S Studio preserves the source drawing as vector PDF. It does not redraw the design, rasterize
whole pages, silently rescale clean drawings, or repair a malformed export by approximation.

Lossless means:

- page dimensions, orientation, MediaBox, and effective CropBox remain correct;
- vector paths, lineweights, line types, colors, fills, hatches, clipping, and transparency remain;
- extractable text remains text where the producer emitted text;
- fonts do not silently substitute into unreadable output;
- page order and multi-page source order remain stable;
- a full-page JPEG/PNG fallback is not introduced;
- project data, frame, grid, title, and title block are independent Studio vector overlays.

## Placement modes

### SourceAsIs

Studio imports source PDF pages directly. It preserves source boxes, orientation, dimensions, and
order. It performs no rasterization or resizing.

### PreserveDrawingSpace

Studio imports the source page as a PDF Form XObject. The source dimensions must match the target
drawing area within `0.75 mm`. The placement matrix must be 1:1; translation is allowed, scaling is
not. A mismatch stops the build and asks for a correct connector export.

### Explicit scaling modes

`FitDrawingArea` or crop/fill behavior may be used only when a template/user explicitly requests
it. Such scaling must be visible in build metadata and auditable in a released revision. It is not
the default and must never hide an incorrect clean drawing-space export.

## Font policy

- Studio-generated concept-design text uses Arial unless an approved page template says otherwise.
- Required fonts must resolve before a controlled release build.
- A missing font produces a visible warning or controlled failure according to page criticality.
- Silent glyph loss or unreadable Mongolian text is a release-blocking defect.

## Structural inspection

`PdfVectorQualityInspector` records and compares:

- physical page and PDF boxes;
- ordered content operators and content-stream SHA-256;
- text/path paint operator presence;
- Image and Form XObject counts;
- imported Form bounding boxes and placement matrices.

A full-page image without corresponding vector/text structure fails the vector gate. A thumbnail or
preview raster is allowed only as UI cache and never becomes canonical album content.

## Golden acceptance matrix

Reference coverage includes:

- A3 landscape and A4 portrait;
- custom and mixed page sizes;
- multi-page input and stable order;
- Mongolian/English text;
- thin, medium, and bold linework;
- hatch, solid fill, clipping, masking, and transparency;
- rotated text, dimensions, and annotation;
- complex Revit and AutoCAD host exports;
- `SourceAsIs` and 1:1 `PreserveDrawingSpace`.

Structural tests are mandatory. Fixed-DPI visual golden comparisons are added for host-specific
reference files where renderer fidelity cannot be proven structurally alone.

## Canonical output

Album build revalidates every package immediately before composition, writes to a temporary PDF,
and atomically replaces the canonical file only on full success. Preview caches use separate files
and must not lock or mutate the canonical PDF.

Every deliverable revision records its PDF SHA-256. Released and archived hashes are immutable.

## Failure policy

Do not stretch, skip, rasterize, substitute, or continue silently. Report the source, sheet, expected
geometry, observed geometry, and corrective export action. A golden reference may change only with
an intentional, reviewed contract or design change.
