# Erk-S Studio Architecture

## Product boundary

Erk-S Cloud owns canonical project identity, organization assignments, collaboration,
notifications, audit records, and published revisions. Erk-S Studio owns the local project
workspace, source bindings, package intake, page composition, preview, and deliverable build.
Erk-S Revit and CityGen AutoCAD own authoring and vector export only.

```text
RVT / DWG (local; never uploaded)
        |
        v
Erk-S Revit / CityGen AutoCAD
        |  vector PDFs + schema-v4 manifest + SHA-256
        v
project/sources/<source>/deliveries/<package>/
        |
        v
SheetPackageAcceptanceValidator
        |  fail closed, package atomic
        v
SheetLibrary (verified records only)
        |
        v
AlbumBuilder -> IAlbumPdfWriter -> canonical vector album PDF
        |
        v
DeliverableRevisionLifecycle (Draft -> Released -> Archive)
        |
        v
Cloud ERA v1 (metadata + manifest + controlled PDF revision)
```

## Project ownership

```text
ProjectWorkspace (.erksproject)
|-- Foundation
|   |-- Initiation basis
|   |-- Planning task (ATD)
|   `-- Stage-scoped design-organization snapshot
|-- Participants and roles
|-- Sources
|   `-- Local binding + accepted delivery metadata
|-- Deliverables
|   |-- Albums (.erksalbum + PDF revisions)
|   `-- Reports
`-- Archive (immutable snapshots)
```

The Studio start screen shows projects only. Source, album, report, and archive workspaces
exist only after a project is opened. `Cloud` and `Local` describe origin and sync state, not
different project models.

## Trust boundaries

### Connector input

Every manifest and path is untrusted. Schema-v4 intake validates identifiers, package scope,
relative paths, reparse points, SHA-256, PDF structure, page count, physical dimensions,
inline format geometry, and clean drawing-space dimensions. The package is accepted as a whole
or rejected as a whole. Rejection cannot delete or replace verified state.

### Native file custody

`DocumentPath` is a local binding hint, not uploaded content. Cloud source custody changes only
who may maintain metadata and bind a replacement local file. It does not transfer the native
file, copyright, payment rights, authorship, or contractual responsibility.

### Cloud state

Cloud ERA is canonical for shared project state. Project mutation uses optimistic concurrency;
source-package and revision operations use stable idempotency identities. Studio retains pending
state after a timeout or partial failure and refreshes canonical state before declaring success.

### Product updates

Product updates use HTTPS, SHA-256, PE validation, Windows Authenticode chain/revocation checks,
and an exact publisher match. Development builds may use loopback HTTP; production builds may not.

## Package and format model

The current sheet package schema is version 4. One delivery folder contains one or more PDF
files and a final `*.erks-sheets.json` manifest. `Delta` packages update selected sheets only;
`FullSnapshot` packages describe the complete current set for one stable source and may remove
only that source's omitted sheets.

`PageFormatSpec` is parametric geometry in millimetres from the page top-left. It declares the
physical page, drawing area, sheet-title area, title-block area, border/grid flags, orientation,
binding edge, and a geometry hash. It is not CAD geometry and is shared by every connector.

## PDF composition

- `SourceAsIs` imports source pages directly and preserves their page boxes and order.
- `PreserveDrawingSpace` imports a vector Form XObject at 1:1 scale and permits translation only.
- A clean drawing-space mismatch greater than the contract tolerance stops the build.
- Studio-generated cover/document pages and official page information are vector content.
- Album output is written to a temporary file and atomically replaces the canonical PDF only
  after every source is revalidated.

The preview opens a copied/cache representation, so WebView/PDF-reader locks do not block a
canonical rebuild.

## Cloud contract

```text
StudioAccountService (application facade)
        |
        +-- ICloudEraContractClient
        |      `-- CloudEraGeneratedContractClient
        |              `-- OpenAPI-generated CloudEraGeneratedClient
        |
        +-- narrow UI boundaries (projects, organizations, collaboration, albums)
        +-- centralized session refresh and ICredentialStore
        `-- specialized multipart upload and concurrency operations

Cloud ERA base:       /api/cloud-era/v1
Capabilities:         /api/cloud-era/v1/capabilities
OpenAPI document:     /api/cloud-era/openapi/v1.json
```

The generated client is the active transport for typed project, design-package, album, and
source-package routes; it applies the current server URL, bearer token, and relationship-boundary
policy to each request. Multipart PDF upload and `If-Match` updates remain explicit wrappers
because those request shapes require specialized handling. The generated client and committed
OpenAPI snapshot are deterministic CI inputs. Capability negotiation requires API major-version
compatibility and the features needed by the current workflow. The removed
`ErkS.Platform.Publishing` project and legacy `/api/platform/albums` contract are not part of the
active architecture.

## Revision lifecycle

```text
Draft -> ReadyForReview -> InReview -> Approved -> Released
  ^              |             |
  |              +-> ChangesRequested -> Superseded
  +-------------------------------------- new child revision

Released -> Superseded -> Archived
Released ----------------> Archived
```

Each revision pins its PDF hash, source package IDs, foundation version, company snapshot,
page count/size summary, creator, timestamps, review state, approval record, and audit note.
`Approved` is workflow approval; it is not a statutory signature. Released PDF content cannot
be edited. A later build creates a child revision and preserves the released snapshot.

## Module dependencies

```text
ErkS.Platform.Contracts
        ^
        |
ErkS.Platform.Core <--- ErkS.Platform.Pdf
        ^                    ^
        |                    |
ErkS.CloudEra.Client     ErkS.Studio.App
        ^                    ^
        +--------------------+
                 |
             ErkS.Studio host
```

Core has no WPF dependency. PDF composition is behind `IAlbumPdfWriter`. UI workspaces consume
canonical core state and service boundaries; they do not define package trust or cloud authority.

## Verification layers

1. Unit and regression tests for contracts, security, lifecycle, and sync policy.
2. Structural vector-PDF golden tests for page boxes, operators, Form/Image XObjects, and 1:1 matrices.
3. Canonical package acceptance for actual Revit and AutoCAD exports.
4. WPF product publish smoke and payload scan.
5. Signed release gate and installer smoke installation.
6. Scheduled 100/500/1000-sheet and 500-page performance regression.

See the linked contracts in the repository README for normative details.
