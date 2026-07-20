# Release Process

Status: required product-release procedure

## Release set

A compatible product release may contain:

- Erk-S Studio;
- Erk-S Revit connector;
- CityGen AutoCAD connector;
- Erk-S Cloud server/site contract changes.

Each component has its own version. Release notes must name the compatible schema and Cloud API
major version. A Studio release that requires new connector metadata must not be published without
the corresponding connector release.

## Preconditions

1. Working-tree scope is reviewed; unrelated user data is not included.
2. Unit, integration, security, vector-PDF, OpenAPI, and generated-client tests pass.
3. Actual Revit and AutoCAD reference packages pass `ErkS.PackageAcceptance`.
4. Release build and WPF publish smoke pass.
5. Performance report has no unexplained regression.
6. `VERSION`, changelog/release notes, README, architecture, and contracts agree.
7. A production code-signing certificate with private key and publisher `Erk-S LLC` is available.
8. Server release storage and backup are verified before replacing a catalog entry.

## Build Studio

Use the product script; do not package a development output folder manually.

```powershell
$env:ERKS_CODE_SIGN_CERT_THUMBPRINT = '<production certificate thumbprint>'
src\scripts\Publish-Studio-Demo.ps1 `
  -ReleaseVersion V0.001.11 `
  -AssemblyVersion 0.0.1.11
```

The script:

- publishes self-contained `win-x64` product binaries with DevUpdate disabled;
- strips debug-only files;
- rejects project/native/dev-marker/test-customer data;
- verifies executable product metadata;
- signs and verifies the product executable;
- builds the setup executable without IExpress;
- signs and verifies the setup;
- smoke-installs to an isolated directory;
- scans the installed payload again;
- emits `release.json` with hash, size, publisher, certificate, and timestamp metadata.

Any failed gate stops the release. Do not bypass a gate or publish an unsigned setup.

## Connector acceptance

Run each host-specific exporter against maintained reference files, then validate every emitted
manifest using the canonical Studio validator. Acceptance includes physical dimensions, hash,
vector structure, format geometry, stable source identity, and full/delta semantics.

The connector package must not contain sample native files, user paths, credentials, or project
data. Pair the release notes with the required Studio sheet-package schema.

## Server/API acceptance

```powershell
dotnet build src\ErkS.LicenseServer\ErkS.LicenseServer.csproj -c Release
dotnet test tests\ErkS.LicenseServer.Tests\ErkS.LicenseServer.Tests.csproj -c Release
```

The generated OpenAPI snapshot must be committed and the Studio generated-client verification must
pass. Back up the external runtime data root before deploying. Source control must not contain its
credentials, tokens, release payloads, or production data.

## Publish and verify

1. Upload the exact signed artifact whose SHA-256 appears in `release.json`.
2. Publish release notes, compatibility, hash, file size, and rollback version.
3. Verify the public catalog returns the same hash and HTTPS download URL.
4. Install on a clean Windows test account.
5. Verify sign-in, project discovery, generated-page-only album, connector intake, album build,
   cloud sync, update check, and uninstall.
6. Keep the previous signed release available until production smoke verification completes.

## Rollback

Rollback publishes an earlier immutable signed artifact and restores compatible server code/data
from an approved backup. Never recreate an old version number with different bytes. Record reason,
time, operator, affected versions, and recovery verification.

## Source history

After the exact release commit is tested, create an annotated immutable tag according to
`VERSIONING.md`. Build artifacts and runtime data remain outside Git history.
