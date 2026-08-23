# Studio → Erk-S-Server: бүх HTTP дуудлагын тайлан

Хүлээн авагч: SRV агент (уялдааны аудитад). Гаргасан: STU агент, 2026-08-23.
"SAS" = `src/src/ErkS.Studio.App/StudioAccountService.cs`.

## 1. Endpoint бүрэн жагсаалт (метод, зам, дуудлагын байршил)

### Session / license (Cloud ERA-ийн гадна)
| Метод | Зам | Байршил |
|---|---|---|
| POST | `/api/license/activate` | SAS:244-248 |
| POST | `/api/studio/session` | SAS:267-271 |
| POST | `/api/studio/session/refresh` | SAS:1752-1756 |
| GET | `/api/studio/profile/photo` | SAS:1394 |
| GET | `/api/cloud-era/v1/capabilities` | `ErkS.CloudEra.Client/CloudEraCapabilitiesClient.cs:28-29` |

### Projects
GET/POST `/api/cloud-era/v1/projects` (SAS:308, :1527 — generated client-ээр);
GET `…/projects/{id}` (SAS:317); GET + `If-None-Match` (SAS:341-348, 304→өөрчлөлтгүй);
DELETE `…/projects/{id}` JSON body-той (SAS:471-487);
PUT `…/{id}/information` (SAS:369-373, If-Match); PUT `…/{id}/building-composition`
(SAS:392-398, If-Match); POST/DELETE `…/{id}/foundation/client-logo` (SAS:430-438,
:453-458, If-Match); PUT `…/{id}/design-organization` (SAS:1540); GET
`…/{id}/design-organization/logo` (ShellView.ProjectBrowser.cs:250-253 → SAS:900-927);
POST `…/{id}/stages` (SAS:1554); PUT `…/{id}/concept-architect` (SAS:622);
PUT `…/{id}/participants/{pid}/roles` (SAS:599); DELETE `…/{id}/participants/{pid}` (SAS:582-587).

### Organizations
GET/POST `…/organizations` (SAS:749-751, :767-770); PUT `…/organizations/{id}`
(SAS:785-788 — токен **body** `BaseConcurrencyToken`); DELETE (SAS:832-836, If-Match);
POST/DELETE `…/{id}/logo` (SAS:867-876, :890-896, If-Match);
POST `…/{id}/registry-imports` (SAS:801-808); GET `…/registry-imports/{importId}`
(SAS:817-820, 2 сек poll ShellView.Companies.cs:1631-1668);
POST `…/{id}/project-creation-grants` (SAS:711-715).

### Collaboration
`…/accounts/lookup` (SAS:1610); `…/project-roles` (SAS:505);
`…/project-membership-invitations` GET/POST/accept/decline/DELETE (SAS:515-573);
`…/project-membership-exit-requests` GET/POST/approve|decline (SAS:637-665 —
шийдвэр нь path segment); `…/project-creation-grants` GET/DELETE/projects (SAS:700-740).

### Sources / documents / files
POST `…/projects/{id}/source-packages` (SAS:1079); DELETE `…/source-packages/{sourceId}`
(SAS:1095-1104 — **If-Match утга нь sourceId**, concurrency токен биш);
PUT `…/sources/{sourceId}/custodian` (SAS:683-693, body token);
GET `…/projects/{id}/documents` (SAS:937-941); PUT `…/documents/{docId}/current-files`
multipart (SAS:983-990); GET `…/design-packages` (SAS:1053);
GET `/api/cloud-era/v1/files/{fileId}` (SAS:1008-1014 ≤25MB баримт; SAS:1303-1309
≤250MB альбом PDF, SHA-256 шалгалттай SAS:1330-1387).

### Albums
GET `…/albums` (SAS:1042); POST `…/albums/ensure` (SAS:1065);
POST `…/albums/{aid}/revisions` multipart ≤20MB (SAS:1172-1207);
POST `…/revisions/uploads` + PUT `…/chunks/{n}` + POST `…/complete`
(`CloudEraChunkedAlbumUploader.cs:51-118` — 8MiB chunk, X-Chunk-SHA256, resume,
идемпотент complete); PUT `…/revisions/{rid}/component-manifest` (SAS:1220-1234);
PUT `…/albums/{aid}/components` multipart ≤32 (`CloudEraAlbumComponentUploader.cs:94-104`).

### Sheet comments (бүгд гараар бичсэн)
GET/POST `…/projects/{id}/sheet-comments` (SAS:1968-1986);
POST `…/sheet-comments/{cid}/replies` (SAS:1996-2000); POST `…/{cid}/status`
(SAS:2010-2014); DELETE `…/{cid}` (SAS:2025-2036). Capability gate, ETag, retry алга.

### Chat
GET `…/projects/{id}/chat?take=&peerEmail=` (SAS:1422-1425, 12 сек poll);
POST `…/chat/messages` multipart (SAS:1455-1461); POST `…/messages/{mid}/reactions`
(SAS:1481-1489); asset GET — серверийн өгсөн зам, `api/cloud-era/v1/projects/`
prefix guard-тай (SAS:1492-1520, ≤15MB).

### Update / catalog (Cloud ERA-ийн гадна, OpenAPI snapshot-д байхгүй)
GET `/api/updates/latest?productCode=&currentVersion=` (`StudioUpdateService.cs:98`);
GET `/api/products/catalog` (`StudioProductCatalogService.cs:177`);
GET `/api/installers/latest?productCode=` (:205-207); зураг — серверийн өгсөн
дурын absolute URL (`StudioSiteImageCache.cs:65-72`).

## 2. DTO-ийн гарал

Гурван давхарга, хуваалцсан assembly алга: (а) NSwag generated
`ErkS.CloudEra.Client/Generated/CloudEraGeneratedClient.g.cs` (68 үйлдэл,
snapshot нь серверийн OpenAPI-тай byte-ижил); (б) түүний 11-ийг л ашигладаг
wrapper `CloudEraGeneratedContractClient.cs` — DTO-г JSON round-trip-ээр хөрвүүлдэг;
(в) гараар бичсэн `StudioCloudContracts.cs` (~78 төрөл) — SAS-ийн ~50 дуудлага
бүгд үүн рүү шууд bind хийдэг. ErkS.Online/CloudEraContracts-ийг Studio шууд
reference хийдэггүй — **хуулбар/parallel гэрээ** (drift 2 газар илэрсэн:
`serverTimeUtc`, `currentOrganizationConcurrencyToken` талбарууд Studio талд алга).

## 3. Нэвтрэлт

Хоёр алхамт нэвтрэлт: `POST /api/license/activate` → `POST /api/studio/session`
(нууц үгтэй), дараа нь capabilities татдаг (SAS:220-303). Refresh:
`POST /api/studio/session/refresh` **нууц үггүй** — licenseId+activationId+
deviceFingerprint-ээр (SAS:1736-1779). Токен зөвхөн санах ойд; license/activation
id Windows Credential Manager (CRED_PERSIST_LOCAL_MACHINE, SAS:2340-2432);
metadata (сервер URL, имэйл, нэрс, license төрөл/дуусах хугацаа) plaintext
`%LOCALAPPDATA%\Erk-S Studio\account.json`. Токен дуусахаас 2 минутын өмнө
proactive refresh (SAS:1692-1727, single-flight). **401-д тусгай handling огт
алга** — генерик алдаа болдог, retry хийдэггүй.

## 4. Cloud ERA v1 хэрэглээ

- Capabilities: нэвтрэлт + refresh бүрт татна; major version "1.0" таарах ёстой;
  Require: `projects`, `album-revisions`, `optimistic-concurrency`,
  `idempotent-sync` (SAS:1789-1792) + үйлдэл бүрийн key-ууд. 15 key зарладаг —
  таны 16-аас `dan-organization-registry-import-v1` алга, гэхдээ registry-import
  endpoint-уудыг **gate-гүй** дууддаг (SAS:802, 818) — ДАН тохируулаагүй сервер
  дээр түүхий 503 (бидний аудитын F2, STU засна).
- ETag/If-Match: project read-д If-None-Match/304; project info/composition/logo,
  org delete/logo-д жинхэнэ If-Match; org PUT, альбом/баримт/source/custodian
  бүгд body/form token; source retire-ийн If-Match утга нь sourceId.
- Альбом upload: chunked session бүрэн хэрэгжсэн (resume, X-Chunk-SHA256,
  complete идемпотент, локал/серверийн SHA тулгалт); >20MB бөгөөд
  `chunked-album-uploads-v1`-гүй бол татгалзана. `Idempotency-Key` header огт
  ашигладаггүй — идемпотентыг ManifestId/ContentHash/revision SHA семантикаар хийдэг.
- Sheet comments/marks: дээрх 5 route; DTO-ууд серверийнхтэй 1:1 (бид тулгасан);
  concurrency болон capability gate байхгүй.

## 5. Лиценз/сунгалт

Studio лицензийг локал шалгадаггүй: sign-in дээр `activate`-ийн `IsValid` л
шалгана; `LicenseType`/`LicenseExpiresAtUtc`-ийг зөвхөн дэлгэцэд харуулдаг
(ShellView.cs:1924), **хугацаа дуусахыг хаана ч enforce хийдэггүй** — эрх бүхэлдээ
серверийн authorization (CanManage/Scopes flags + 403/404). Update гинж:
HTTPS шаардлага → SHA-256 → PE header → WinVerifyTrust (revocation chain) →
publisher "Erk-S LLC" exact + pinned-untrusted-root exception (толгойлуулсан
thumbprint, prod: A8A0A7C1…36D8A).

## 6. Алдааны боловсруулалт

`StudioCloudApiError {Code, Message, TraceId, CurrentSourceId, CurrentRevisionId,
FieldErrors}` — таны `CurrentOrganizationConcurrencyToken` талбарыг **загварладаггүй**,
`X-ErkS-Current-*` header-үүдээс зөвхөн `X-ErkS-Operation-Id`-г уншдаг (бидний F3,
STU засна). Статус тус бүр: 401 — тусгай зүйлгүй; 403/404 — «төслийн access
дууссан» гэж төслөө хааж жагсаалтаас хасна (локал mirror хадгална); 404/405
project-info PUT дээр — «хуучин сервер» гэж локалд Pending хадгална; 409/412 —
локал засвараа хадгалж canonical-ыг дахин татаж MarkConflict (412=
`project_concurrency_conflict`, 409=`cloud_sync_conflict`), альбомын publish
409/412-т бүтэн rebuild давталт; 413 — component merge-д файлын нэрстэй тусгай
мессеж; 5xx/408/429 — **зөвхөн chunk upload retry-тэй** (3 удаа, 250ms×n линийн
backoff), бусад бүх endpoint нэг оролдлого. Offline: чимээгүй poll-ууд алдааг
залгидаг, source retirement-д durable outbox бий, түр тасалдлыг access-хасалт
гэж андуурдаггүй (ShellView.Collaboration.cs:348-353).

## Хавсралт

Бүрэн аудит (findings F1-F25, зэрэглэл, засах тал):
`Erk-S Studio\docs\INTEGRATION-AUDIT-2026-08-23.md`.
