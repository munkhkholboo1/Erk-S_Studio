# Erk-S Studio

Erk-S Studio is the desktop project and document-control application for Erk-S Cloud ERA.
It receives verified vector PDF sheets from Erk-S Revit, CityGen AutoCAD, CityGen, and
manual PDF sources; adds Studio-owned page information; builds controlled albums and
reports; and synchronizes immutable deliverable revisions with `erk-s.mn`.

## Product terminology

- **Erk-S Cloud** - `erk-s.mn` and the canonical server project.
- **Erk-S Studio** - desktop project, source, album, report, and revision control.
- **Erk-S Revit** - Revit sheet exporter and Studio connector.
- **CityGen AutoCAD** - AutoCAD layout authoring and Studio connector.

RVT, DWG, and other native authoring files remain on the participant's own device or
storage. Studio and Erk-S Cloud do not upload, transfer, or take custody of them.

## Core workflow

1. Sign in with an Erk-S account and activate the Studio license for the device.
2. Open a cloud mirror or create a local project workspace.
3. Confirm project foundation, planning task, design organization snapshot, and team.
4. Add project-owned sources and bind each source to its local authoring file.
5. Export sheets from Revit/AutoCAD as schema-v4 sheet packages.
6. Studio validates the complete package, hash, path, page size, and format geometry.
7. Build the album only from verified source sheets and Studio-generated pages.
8. Create a Draft revision, review/approve/release it, then synchronize it to Erk-S Cloud.

An invalid package is rejected atomically and cannot replace an earlier verified sheet.
Released and archived revisions are immutable; a later change creates a new revision.

## Repository layout

- `src/src/ErkS.Platform.Contracts` - schema-v4 sheet package and page-format contract.
- `src/src/ErkS.Platform.Core` - project workspace, intake, reconciliation, album build,
  revision lifecycle, security policy, and performance policy.
- `src/src/ErkS.Platform.Pdf` - vector PDF composition and structural quality inspection.
- `src/src/ErkS.CloudEra.Client` - OpenAPI-generated Cloud ERA client and capability policy.
- `src/src/ErkS.Studio.App` - WPF project workspaces and cloud orchestration.
- `src/src/ErkS.Studio` - thin executable host and development module loader.
- `src/tools/ErkS.PackageAcceptance` - canonical connector-package validator.
- `src/tools/ErkS.Studio.Performance` - repeatable performance regression harness.
- `src/tests/ErkS.Platform.Core.Tests` - unit, integration, security, and PDF tests.

## Project storage

```text
Studio Projects/<project-code>/
|-- project.erksproject
|-- sources/<source>/deliveries/
|-- albums/<album>.erksalbum
|-- albums/<album>.pdf
|-- reports/
`-- archive/
```

Source, album, report, and archive data never live outside an opened project workspace.
Legacy project-container `.erksalbum` files are migrated without modifying their source file.

## Build and test

```powershell
dotnet restore src\ErkS.Studio.slnx
dotnet build src\ErkS.Studio.slnx -c Release
dotnet test src\tests\ErkS.Platform.Core.Tests\ErkS.Platform.Core.Tests.csproj -c Release
src\scripts\Test-CloudEraGeneratedClient.ps1
```

Run the Studio development host:

```powershell
dotnet run --project src\src\ErkS.Studio
```

`DevUpdate` and its dev bar exist only in a development build. Product builds created with
`StudioProductBuild=true` do not expose DevUpdate; users receive normal signed product updates.

## Connector acceptance

Validate any exported package with the same fail-closed boundary used by Studio:

```powershell
dotnet run --project src\tools\ErkS.PackageAcceptance\ErkS.PackageAcceptance.csproj `
  -c Release -- <path-to-manifest.erks-sheets.json>
```

The Revit and AutoCAD connector repositories also contain host-specific acceptance scripts.
Those scripts must pass before a connector release is paired with a Studio release.

## Cloud API

The canonical API base is `/api/cloud-era/v1`. Studio negotiates
`/api/cloud-era/v1/capabilities` after sign-in and does not infer feature support from 404
responses. The server OpenAPI document is `/api/cloud-era/openapi/v1.json`; the committed
snapshot generates `ErkS.CloudEra.Client` deterministically.

Project updates use `ETag`/`If-Match`. Source registration and album-revision upload are
idempotent. A partial or conflicting sync is never marked `Synced`.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Sheet Package Contract](docs/SHEET-PACKAGE-CONTRACT.md)
- [Cloud API Contract](docs/CLOUD-API-CONTRACT.md)
- [Security Model](docs/SECURITY-AND-VECTOR-PIPELINE.md)
- [PDF Quality Standard](docs/PDF-QUALITY-STANDARD.md)
- [Release Process](docs/RELEASE-PROCESS.md)
- [Update Signing Process](docs/UPDATE-SIGNING.md)
- [TDD Contribution Guide](docs/CONTRIBUTING-TDD.md)
- [Relationship Boundary](docs/RELATIONSHIP-BOUNDARY.md)
- [Source File Custody Policy](docs/SOURCE-FILE-CUSTODY.md)
- [Versioning and Backup](docs/VERSIONING.md)

## Release rule

Production setup and update executables must be SHA-256 verified, Authenticode signed by
`Erk-S LLC`, and timestamped. The release script fails closed when the certificate, publisher,
signature, product metadata, smoke install, or payload-content checks fail. Credentials,
licenses, local projects, native files, and test customer data are forbidden from release
artifacts and source history.
