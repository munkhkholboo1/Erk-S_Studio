# Cloud API Contract

Status: normative Studio/server integration contract
Rewritten: 2026-08-23, against the server's `CloudEraApiContract.cs` and the
integration audit (`INTEGRATION-AUDIT-2026-08-23.md`). Server review: requested.

API version: `1.0` (major-only compatibility; the server snapshot may name it
`1.0.0` — clients compare the major component).

## Canonical endpoints

- API base: `/api/cloud-era/v1`
- Capabilities: `/api/cloud-era/v1/capabilities`
- OpenAPI: `/api/cloud-era/openapi/v1.json` — published for tooling; Studio does
  not fetch it at runtime. The committed snapshot under `src/contracts` is copied
  from the server repository at build time and MUST stay byte-identical to the
  server's emitted document.

Session, licensing, updates and the program catalog are deliberately OUTSIDE the
Cloud ERA base and are covered in "Non-Cloud-ERA channels" below.

## Capability negotiation

Studio fetches capabilities inside sign-in and after every session refresh, and
requires a compatible API major version before anything else. The server
advertises exactly these 16 keys; the table states what Studio does with each.

| Key | Studio-ийн хэрэглээ |
| --- | --- |
| `projects` | Required at every session load |
| `album-revisions` | Required at session load and before album reads/uploads |
| `optimistic-concurrency` | Required at session load and before every guarded write |
| `idempotent-sync` | Required at every session load |
| `source-packages-v4` | Required before source-package registration/retirement |
| `source-package-cas-v1` | Required before source-package registration |
| `album-component-merge-v1` | Required before component merge |
| `contributor-owned-components-v1` | Required before component merge |
| `chunked-album-uploads-v1` | Soft-checked; without it albums over the single-shot limit are refused |
| `participant-role-management` | Required before participant-role updates (also gates UI) |
| `concept-architect-assignment` | Required before concept-architect assignment |
| `dan-organization-registry-import-v1` | Required before state-registry (ДАН) import calls |
| `organizations` | Informational — organization access is enforced server-side |
| `collaboration` | Informational — collaboration access is enforced server-side |
| `relationship-boundary` | Informational — the acknowledgement header is sent by policy, not by key |
| `native-source-remains-local` | Informational — the native-source rule is enforced by both sides regardless |

A missing required capability or incompatible major version is a controlled
contract error. Studio MUST NOT catch a 404 on a Cloud ERA route and infer
whether a feature exists. Single sanctioned exception: a development build
talking to a loopback server whose `/capabilities` answers 404 may fabricate the
legacy capability map (`StudioAccountService.CreateLegacyLoopbackCapabilities`);
this path is unreachable in production builds and MUST stay that way.

## Client architecture

The server emits an OpenAPI snapshot at build time; Studio commits a matching
snapshot under `src/contracts` and generates `ErkS.CloudEra.Client` with the
repository-local NSwag tool. CI regenerates the client and fails on drift.

The actual client is layered, and this is deliberate:

- **Generated client** (`CloudEraGeneratedClient.g.cs`) — the full operation
  surface. `CloudEraGeneratedContractClient` wraps the typed subset Studio uses
  through it (project list/create/detail, design-organization assignment, stage
  advance, participant roles, concept architect, design packages, albums,
  ensure-album, source-package registration).
- **Hand-written calls** (`StudioAccountService`, `CloudEraChunkedAlbumUploader`,
  `CloudEraAlbumComponentUploader`) — everything needing `If-Match`,
  `If-None-Match`, multipart forms, or streamed binaries, plus session-critical
  paths kept explicit: organizations, collaboration, controlled documents,
  chat, sheet comments, file downloads, project delete, conditional refresh.
- **Presentation DTOs** (`StudioCloudContracts.cs`) — Studio-side mirror types.
  Every mirror type MUST stay field-compatible with the OpenAPI schema it
  mirrors; adding a server response field that Studio needs means adding it to
  the mirror in the same change. Hand-written wrappers MUST NOT invent an
  endpoint the server does not expose.

```powershell
src\scripts\Generate-CloudEraClient.ps1
src\scripts\Test-CloudEraGeneratedClient.ps1
```

## Operation surface (Cloud ERA)

Grouped inventory of everything Studio calls. Full file:line detail:
`docs/TASK-srv-http-inventory.md`.

- **Projects**: list/create/detail, conditional detail refresh
  (`If-None-Match`/304), delete (JSON body), information (`If-Match`),
  building-composition (`If-Match`), design-organization assignment, stages,
  client logo add/remove (`If-Match`), design-organization logo download.
- **Organizations**: list/create; update (concurrency token in the request
  body's `baseConcurrencyToken`); delete and logo add/remove (`If-Match`);
  state-registry (ДАН) import start/poll; project-creation grants.
- **Collaboration**: account lookup, project roles, membership invitations
  (create/accept/decline/revoke), exit requests (create/approve/decline),
  participant role updates, participant removal.
- **Sources / documents / files**: source-package registration
  (`expectedBaseSourceId` in body), retirement (`If-Match` whose value is the
  source id — an existence guard, not a concurrency token), custodian transfer
  (tokens in body), controlled documents list/replace (multipart with
  `expectedDocumentVersion` + `projectConcurrencyToken`), design packages,
  file download by id with SHA-256 verification.
- **Albums**: list/ensure, single-shot revision upload (multipart, small
  albums), chunked upload session (start → per-chunk PUT with `X-Chunk-SHA256`
  → complete; resumable; complete is idempotent), component-manifest update,
  component merge (multipart; `expectedRevisionId` + `projectConcurrencyToken`
  as form fields).
- **Sheet comments**: list, create, reply, status change, delete. Every
  mutation returns the whole comment list; there is currently no per-comment
  concurrency and no dedicated feature key — additions here require a
  server-coordinated contract change.
- **Project chat**: history (polled), send message (multipart attachment),
  reactions, attachment/participant-photo download. Chat asset paths supplied
  by the server MUST start with `api/cloud-era/v1/projects/` to be fetched.

## Authentication and authorization

- Device activation (`/api/license/activate`) and the Studio session
  (`/api/studio/session`) are separate operations; both carry the password.
  Session refresh (`/api/studio/session/refresh`) re-proves identity with
  licenseId + activationId + deviceFingerprint, without the password — a
  deliberate design; the trade-off and its residual risk are recorded in
  `Erk-S-Server/docs/INTEGRATION-AUDIT-2026-08-23.md` §7.1 (rate-limited
  server-side; revocation is device unbind).
- Access tokens live 15 minutes; Studio refreshes when less than 2 minutes
  remain, single-flight, before every authenticated call.
- Storage: the access token is held in memory only. LicenseId/activationId/
  deviceFingerprint live in Windows Credential Manager. Non-secret session
  metadata (server URL, e-mail, display names, profile-image URL, licence
  type/expiry) lives in `%LOCALAPPDATA%\Erk-S Studio\account.json`; that file
  MUST never contain a password or token.
- **Same-origin rule**: a request that carries the bearer token MUST resolve to
  the session server's own origin. A server-supplied URL (profile image,
  organization logo, chat asset) that resolves anywhere else is refused
  (`StudioAccountService.TryBuildSameOriginUri`).
- The server enforces account, organization, role, scope, and
  relationship-policy rules; entitlement flags on DTOs (`canManage`, scopes)
  drive UI enablement only.
- A typed error includes an HTTP status and stable error code when available.

## Error envelope

`{ code, message, traceId, currentSourceId, currentRevisionId,
currentOrganizationConcurrencyToken, fieldErrors }` — mirrored by
`StudioCloudApiError`. `traceId` falls back to the `X-ErkS-Operation-Id`
response header. On an organization 412 the server returns the current
canonical token in the body; Studio keeps it so a reviewed retry does not
require a full organization re-list.

## Optimistic concurrency

Canonical project reads and successful writes return a concurrency token/`ETag`.

- Project-level mutations send the last canonical token via `If-Match`.
- Organization update, album uploads/merges, controlled documents, custodian
  transfer and source registration carry their expected tokens/ids in the
  request body or multipart form fields, as named in the operation surface
  above. These are part of the contract even though they are not HTTP headers.
- Current token: apply the mutation and return a new token. Missing token on a
  protected update: reject. Stale token: return conflict with the current
  canonical context in the error envelope, and preserve server state.
- Studio keeps the local pending edit until the user resolves the conflict.
  A conflict MUST NOT be marked `Synced`.

## Incremental project refresh and local cache

`Cloud-оос шинэчлэх` is a conditional refresh, not a project re-download. Studio
sends the last canonical project token in `If-None-Match`. The server returns
`304 Not Modified` without a body when project metadata has not changed. When
the token changed, Studio downloads canonical metadata and reconciles by stable
identity; binary assets are fetched only when their version key changed, the
local file is missing, or its SHA-256 does not match. Album PDFs use revision
identity plus SHA-256 as the dirty key.

Studio owns only the top-level cache under `<project>\outputs\cloud`. After a
successful refresh it keeps the current PDF and removes older PDFs plus
interrupted `.download`/`.tmp*` files. Project sources, native RVT/DWG files,
and unrelated files are never part of this cleanup.

## Idempotent synchronization

Source-package registration is identified by the stable manifest/package ID.
Album-revision upload is identified by stable revision data and PDF SHA-256.
A retry after timeout MUST return the same canonical record rather than create
a duplicate. There is no `Idempotency-Key` header; idempotency is semantic.

Studio sync order: (1) refresh session + capabilities; (2) refresh canonical
project state; (3) reconcile each pending verified source package; (4) ensure
the cloud album exists; (5) verify the local canonical album PDF and SHA-256;
(6) upload/reconcile the revision; (7) verify server revision identity and
hash; (8) refresh canonical state; (9) mark only confirmed operations as
synchronized. A timeout is an unknown state: check canonical server state
before retrying. Retry with backoff exists only for chunk uploads
(408/429/5xx); every other operation is single-attempt by design.

## Relationship-changing operations

Member invitation/acceptance/removal, exit requests, project-creation grants,
design-organization assignment, and source-custodian transfer require the
current relationship-boundary acknowledgement
(`X-ErkS-Relationship-Boundary`). The header value (policy version) is defined
identically on both sides. The acknowledgement MUST be sent only after explicit
user confirmation; a client path that attaches it unconditionally MUST NOT be
used for relationship-changing routes. The server records actor, action,
counterparty reference, policy version, and timestamp.

See `RELATIONSHIP-BOUNDARY.md` for the neutral platform boundary.

## Native source rule

Cloud source-package APIs accept manifest identity, hashes, sheet metadata, and
controlled PDF deliverables. They MUST reject or omit RVT, DWG, and other
native-source payloads. A custody change updates metadata only; actual file
handover remains off-platform.

## Non-Cloud-ERA channels

These live outside `/api/cloud-era/v1` and outside the OpenAPI snapshot:

- **Session/license**: `/api/license/activate`, `/api/studio/session`,
  `/api/studio/session/refresh`, `/api/studio/profile/photo`.
- **Updates**: `/api/updates/latest` — governed by `UPDATE-SIGNING.md`
  (transport gate, SHA-256, Authenticode chain, publisher pin).
- **Program catalog**: `/api/products/catalog`, `/api/installers/latest` —
  presentation data for the home page. This channel has an explicit designed
  fallback: on any failure (404 included) the built-in product list stands in.
  Downloads are handed to the user's browser, never fetched in-process.
- Site images referenced by the catalog are cached on disk with content-type
  and size limits; these requests never carry the bearer token.

## Contract change policy

- Backward-compatible optional response fields may be added within API v1;
  a Studio mirror DTO gains the field in the same change when Studio consumes it.
- Required-field removal, semantic change, or incompatible route behavior
  requires a new API major.
- A new feature key is added to the server contract, this document's table, and
  `CloudEraFeatures` together, with its enforcement point named.
- Server OpenAPI tests, generated-client tests, and Studio capability tests
  must change together. README, architecture, server contract, and generated
  client must name the same routes and terms.
