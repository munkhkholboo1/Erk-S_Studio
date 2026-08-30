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

## Producer-selected print color

AutoCAD and other authoring connectors may export each logical sheet as `Original`,
`BlackAndWhite`, or `Grayscale`. The producer applies that selection to the vector PDF before
publishing the package and records it as `printColorMode` on the matching sheet entry.

Studio treats the verified PDF as the visual authority. It preserves the baked color operators
when importing the page directly or as a Form XObject and does not perform a second color
conversion. The verified source PDF bytes remain immutable, while album composition preserves
their vector/color operators and appearance across source refresh, rebuild, and cloud
synchronization.

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

## On-screen rendering: a translucent shadow loses coverage

Measured 2026-08-31 while tracing gaps a user saw in the shadows of a general-plan
sheet. The gaps are not in the file: PFA expanded all 255 soft masks in the export
and flood-filled them, finding no enclosed hole. They appear in Studio's preview.

**The path.** `PdfiumDocument.RenderPage` renders through Pdfium
(`FPDF_RenderPageBitmap`) into a white-filled BGRA32 bitmap with
`FPDF_ANNOT | FPDF_LCD_TEXT`, and WPF then scales that bitmap to the size on
screen. Pdfium composites the JPEG and its soft mask itself; Studio never touches
alpha.

**Method.** CityGen exported the same drawing twice - once with translucent
shadows (255 soft masks) and once blended to opaque vector. Rendering both at the
same width and comparing *coverage* - is this pixel darker than paper - isolates
what the alpha path costs, because the two pages carry the same geometry.

A first attempt counted near-white pixels enclosed by grey and found hundreds. The
opaque control returned the same count at the same coordinates, so the measurement
was finding white shapes inside shadowed areas - the drawing, not a defect. Kept
here because the number looked like evidence.

**What the comparison shows.** Across eleven raster widths from 2200 to 2600, the
translucent render is bare where the opaque one is covered far more often than the
reverse - 752/91, 668/199, 767/304 and so on, asymmetric at every width. Most of it
is single pixels along edges. But at some widths a long unbroken run appears:
153 pixels at 2400, 86 at 2410, 92 at 2600, and nothing longer than 22 at 2250,
2350, 2390, 2450, 2500 or 2550. Scaling the coordinates shows 2400, 2410 and 2600
are the same feature at different severities.

So this is a thin feature the translucent path drops at some sampling scales and
keeps at others - present at one pixel size and gone at another, which is the
signature of resampling rather than of the file. Deterministic: repeated runs give
identical numbers.

**2400 is not the cause, though it is where the user meets it.**
`PreviewRenderResolution.FirstPassWidthPx` is 2400, so the first image of any page
is rendered at one of the widths that loses the most. The width is not the
mechanism - several others lose it too, and several nearby ones do not.

**The display stage adds nothing.** Scaling the 2400 raster to 1000 and 1400 pixels
carries the defect through at reduced length (36 and 89 pixels) and introduces no
new one. `BitmapScalingMode` makes no difference at all - `Unspecified`,
`LowQuality` and `HighQuality` give byte-identical results, because
`App.xaml.cs` sets `RenderOptions.ProcessRenderMode = SoftwareOnly` and the mode is
a GPU hint. `SheetMarkupSurface` is the one preview surface that does not set
`HighQuality`, and that turns out not to matter.

**The link to "the quality is terrible, is it still 78 dpi".** The same asymmetry
explains it. A shadow at 40% over white, sampled coarsely, rounds to paper; the
opaque blend does not. The complaint and the gaps are one effect at two
severities, which is why raising the plot DPI did not answer it - the plot was
already at 400.

Not fixed. The candidate directions - first-pass width, render flags, rendering
finer and downsampling - are not chosen yet, and a fix must be measured the same
way rather than argued.
