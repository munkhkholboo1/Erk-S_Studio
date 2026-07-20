# Cloud API Contract

Status: normative Studio/server integration contract

API version: `1.0`

## Canonical endpoints

- API base: `/api/cloud-era/v1`
- Capabilities: `/api/cloud-era/v1/capabilities`
- OpenAPI: `/api/cloud-era/openapi/v1.json`

The active contract does not include the removed `/api/platform/albums` endpoint.

## Capability negotiation

Studio requests capabilities after sign-in and session refresh. It requires compatible API major
version and explicitly advertised features before using them. Current feature keys are:

- `projects`
- `organizations`
- `collaboration`
- `source-packages-v4`
- `album-revisions`
- `optimistic-concurrency`
- `idempotent-sync`
- `relationship-boundary`
- `native-source-remains-local`

A missing required capability or incompatible major version is a controlled contract error.
Studio MUST NOT catch a 404 and infer whether a feature exists.

## Generated client

The server emits an OpenAPI snapshot at build time. Studio commits a matching snapshot under
`src/contracts` and generates `ErkS.CloudEra.Client` with the repository-local NSwag tool.

`CloudEraGeneratedContractClient` is the application wrapper used by Studio for typed project,
design-package, album, and source-package operations. It maps generated DTOs into Studio's
presentation contracts and translates generated HTTP failures into typed account errors.
Specialized multipart upload and `If-Match` operations remain explicit until those complete
header/form contracts are represented by OpenAPI.

```powershell
src\scripts\Generate-CloudEraClient.ps1
src\scripts\Test-CloudEraGeneratedClient.ps1
```

CI regenerates the client and fails on drift. Hand-written application wrappers may add session,
policy, and domain behavior, but MUST NOT invent a second endpoint or DTO contract.

## Authentication and authorization

- Device activation and Studio session are separate operations.
- Credentials are stored through Windows Credential Manager, not in project files or source control.
- Cloud calls use a bearer access token and pass cancellation tokens.
- Session refresh is centralized before an authenticated operation.
- The server enforces account, organization, role, scope, and relationship-policy rules.
- A typed error includes an HTTP status and stable error code when available.

## Optimistic concurrency

Canonical project reads and successful writes return a concurrency token/`ETag`. A project update
MUST send the last canonical token using `If-Match`.

- Current token: apply the mutation and return a new token.
- Missing token on a protected update: reject the mutation.
- Stale token: return conflict, preserve server state, and return current canonical context.
- Studio keeps the local pending edit until the user refreshes or resolves the conflict.
- A conflict MUST NOT be marked `Synced`.

## Incremental project refresh and local cache

`Cloud-оос шинэчлэх` is a conditional refresh, not a project re-download. Studio sends the last
canonical project token in `If-None-Match`. The server returns `304 Not Modified` without a response
body when project metadata has not changed. Studio records the check time but does not rewrite the
canonical mirror or download an album, logo, document, or native source.

When the token changed, Studio downloads canonical project metadata and reconciles fields by stable
identity. Binary assets are fetched only when their version key changed, the local file is missing,
or its SHA-256 does not match. Album PDFs use revision identity plus SHA-256 as the dirty key.

Studio owns only the top-level cache under `<project>\outputs\cloud`. After a successful refresh it
keeps the current PDF and removes older PDFs plus interrupted `.download`/`.tmp*` files. If the
server has no current album revision, all owned album-cache artifacts are removed. Project sources,
native RVT/DWG files, and unrelated files are never part of this cleanup.

## Idempotent synchronization

Source-package registration is identified by the stable manifest/package ID. Album-revision upload
is identified by stable revision data and PDF SHA-256. A retry after timeout MUST return the same
canonical record rather than create a duplicate.

Studio sync order is:

1. refresh/validate the session and capabilities;
2. refresh canonical project state;
3. reconcile each pending verified source package;
4. ensure the cloud album exists even when it has only generated pages;
5. verify the local canonical album PDF and SHA-256;
6. upload/reconcile the revision;
7. verify server revision identity and hash;
8. refresh canonical project state;
9. mark only confirmed operations as synchronized.

A timeout is an unknown state. Studio checks canonical server state before retrying. Partial failure
keeps successful item acknowledgements and pending failures separately.

## Relationship-changing operations

Member invitation/acceptance/removal, exit requests, project-creation grants, design-organization
assignment, and source-custodian transfer require the current relationship-boundary acknowledgement.
The server records actor, action, counterparty reference, policy version, and timestamp.

See `RELATIONSHIP-BOUNDARY.md` for the neutral platform boundary.

## Native source rule

Cloud source-package APIs accept manifest identity, hashes, sheet metadata, and controlled PDF
deliverables. They MUST reject or omit RVT, DWG, and other native-source payloads. A custody change
updates metadata only; actual file handover remains off-platform.

## Contract change policy

- Backward-compatible optional response fields may be added within API v1.
- Required-field removal, semantic change, or incompatible route behavior requires a new API major.
- Server OpenAPI tests, generated-client tests, and Studio capability tests must change together.
- README, architecture, server contract, and generated client must name the same routes and terms.
