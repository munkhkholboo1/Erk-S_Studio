# Erk-S Studio — Уялдааны аудит (2026-08-23)

Захиалагч: «ERK-S Мастер» (даалгавар №2). Гүйцэтгэсэн: STU агент.
Арга: Studio-ийн бүх гадаад холболтын гадаргууг тоолж, хөрш репо тус бүрийн
(PFA, PFR, CGA/CGM/IFG, SRV) бодит кодтой хоёр талаас нь тулгаж шалгав; эргэлзээтэй
цэгүүдийг PFA/PFR/SRV/ONE агент-сешнүүдтэй шууд солилцож баталгаажуулав.
Бодит багцын хамтарсан туршилтыг PFA-тай хийв (§1.4).

Товч дүн: **уялдааны үндсэн цэг 6 бүлэг, 100+ дэд цэг шалгагдаж**, wire-түвшний
эвдрэл (Studio дуудаад сервер нь байхгүй route, hash-ийн зөрүү г.м.) **олдоогүй**.
Илэрсэн 26 finding-ийн дийлэнх нь баримтын хоцрогдол, нэг талын хамгаалалтын
сул тал, далд (latent) таамаглалууд. Аюулгүй байдлын 1 өндөр зэрэглэлийн
finding бий (F1).

---

## 1. Sheet package гэрээ (PFA → STU, PFR → STU)

Норматив: `docs/SHEET-PACKAGE-CONTRACT.md`, `src/src/ErkS.Platform.Contracts/SheetPackage.cs`,
`SheetPackageIo.cs` (fail-closed reader).

### 1.1 PFA (AutoCAD) producer — НИЙЦТЭЙ

Writer: `Erk-S Platform For Autocad\src\autocad\2026-dev\source\AutoCAD_v2\src\sheet-packages\StudioSheetPackage.cs`.

- **schemaVersion 5** — v5-ийн бүх reader шалгалт хангагдсан (pdfPageNumber, page refs).
- **geometryHash canonicalization byte-ижил** — 16 талбарын дараалал, `"R"` invariant
  формат, `Trim().ToUpperInvariant()`, `"x,y,w,h"`, lowercase SHA-256 бүгд таарсан.
  Hash-аар багц унах эрсдэлгүй.
- **Бүх enum literal яг таг**: `"AutoCad"`, `"Delta"`/`"FullSnapshot"`,
  `"Original"`/`"BlackAndWhite"`/`"Grayscale"`, `"Album"`/`"Portfolio"` (Ordinal).
- **Портфолио формат**: mode Portfolio, 4 талдаа 10мм margin, 0 хэмжээт
  sheetTitle/titleBlock, стандарт цаасны матриц — 5 шаардлага бүгд хангагдсан.
- PDF-ээ дуусгаж баталгаажуулснаа манифест бичдэг; SHA lowercase; PC3 матриц ижил.

**PFA-талын зөрүү (F8, Дунд/Бага):**
1. Манифестээ нийтлэхээсээ өмнө reader-ээр round-trip шалгадаггүй (Studio-ийн
   reference writer `SheetPackageIo.cs:695-716` шалгадаг) — format-гүй дуудагч
   гарвал Studio-д quarantine болтол мэдэгдэхгүй.
2. Temp файлын сахилга сул: тогтмол `.tmp` нэр, `finally` цэвэрлэгээгүй.
3. `exportedAtUtc` нь экспортын ЭХЛЭЛ (гэрээ «completed export» гэдэг) —
   snapshot эрэмбэлэлт эхлэлээр явна; AutoCAD зэрэгцээ plot хийдэггүй тул бодит
   эрсдэл бага.
4. sheetId давхардлын шалгалт `.Trim()` хийдэггүй.
5. Хоосон `documentPath`-д throw хийдэг (гэрээгээр optional).

Мөн: `exportMode:"LayoutsAsIs"` — Studio огт уншдаггүй (§5 F20);
`isCleanDrawingSpace` хувилбарыг PFA хэзээ ч гаргаж чадахгүй (одоогоор inert);
PDF Rotate 90/270 хуудсан дээр хоёр талын хэмжээс уншилт зөрөх далд эрсдэл —
одоогоор plot 0° тул inert.

### 1.2 PFR (Revit) producer — v4 хүчинтэй, 4 бодит зөрүү

Writer: `Erk-S Platform For Revit\src\revit\2026-dev\source\Revit\ErkS.Revit.TitleBlocks\StudioSheetPackageExportService.cs` (v4).

Нийцтэй: geometryHash canonicalization byte-ижил; концепц хуудасны геометр
Studio-ийн `BuildingArchitectureConceptPageLayout`-тай тоогоор яг ижил; цаасны
матриц ижил; `"Хуудасны тайлбар"` параметр → `sheetDescription` яг таарсан;
атомик бичилт, UTC, suffix бүгд зөв. `printColorMode`/`destination`/`pdfPageNumber`
талбаргүй нь v4-д хүчинтэй (default-ууд зөв утгатай).

**PFR-талын зөрүү:**
- **F4 (Дунд): Мэргэжлийн марк.** PFR зөвхөн `ЕХ`, `БА`, `ТХ`, `ЕТ` гаргадаг.
  `ТХ` Studio-ийн каталогт байхгүй → ЕХ бүлэгт орно; «Зургийн марк» хоосон бол
  категорийн бүтэн нэр илгээгддэг → мөн ЕХ; Studio-ийн 9 багцын 6 нь (ББ, ХАС,
  ДМ, ЦБУ, ХТ,ДГ, ХД, АУ) Revit-ээс огт хүрэх замгүй. (ДМ/АУ/ХТ,ДГ-ийн 0.001.41
  засвар нь PFA-ийн маркуудад зориулагдсан байсан нь тогтоогдов.)
- **F5 (Дунд): Ажлын зургийн format геометр зохиомол** — PFR өөрийн зурдаг
  фрэймтэйгээ ч, Studio-ийн геометртэй ч таардаггүй тоо илгээдэг; reader хүлээж
  аваад `PreserveDrawingSpace` байрлалд ашигладаг тул контент худал drawing area-д
  тулгуурлан байрлана (чимээгүй буруу өгөгдөл).
- **F6 (Дунд): Шүүсэн олонлогоо FullSnapshot гэж илгээдэг** — ЕТ марктай,
  хэвлэгдэхгүй, Studio-owned хуудсуудыг алгасаад scope-оо FullSnapshot хэвээр
  явуулдаг тул өмнө нь хүргэгдсэн, энэ удаад зүгээр л алгасагдсан хуудас номын
  сангаас устдаг.
- **F7 (Дунд): Pre-publish шалгалтгүй + хэмжээсийн fallback эрсдэл** — Erk-S бус
  title block-той sheet-д A1-landscape default тулгаж манифестэд бичдэг, харин
  PDF нь бодит цаасаараа гардаг тул ±0.75мм шалгалтад бүхэл багц quarantine болно.
  `validate_latest_studio_package.ps1` (канон validator) байдаг ч экспортын замд ороогүй.
- **Studio-талын дагалдах:** F17 — `ShellView.cs:3458`-ийн `?? Number.Split('-')`
  fallback хэзээ ч ажиллахгүй үхмэл код (Discipline нь non-nullable `""`).
- Барилгын identity (`buildingId/Name`) үргэлж хоосон — олон барилгат Revit
  загвар нэг бүлэгт хавтгайрна (omission).

PFR v5 руу шилжих саналыг дэмжсэн (destination v4 дээр ч хүчинтэй гэдгийг
тэмдэглэв; portfolio-ийн pdfPageNumber нь v5 шаардана).

### 1.3 Локал PDF importer (Studio-ийн дотоод producer)

`ErkS.Platform.Pdf\LocalPdfSheetPackageImporter.cs` — `stageId`-д Guid "N" ЭСВЭЛ
stage code гэсэн холимог утга бичдэг (:109-110) → F19.

### 1.4 Бодит багцын хамтарсан туршилт (PFA ↔ STU) — АМЖИЛТТАЙ

PFA бодит pipeline-аараа (AutoCAD 2026 Core Console) 1 Album + 1 Portfolio
хуудастай v5 FullSnapshot гаргаж, Studio-ийн бүрэн замаар шалгав: reader lossless,
альбомын entry → номын сан + концепц альбомын хуудас, портфолио entry → номын
санд ороогүй, альбомд хуудас үүсгээгүй, CadPage item үүсч PDF нь content-addressed
нэрээр төслийн санд хуулагдаж hash таарсан. Мөн manifest-ийн projectId зөрүүтэй
үед Apply-ийн эзэмшлийн хаалт зөв татгалзахыг давхар ажиглав. Re-import identity
туршилт (2 дахь багц) хүлээгдэж байна.

### 1.5 Гэрээний давхаргын hardening тэмдэглэл

- **F16 (Бага):** `packageScope`/`source.application` enum-ууд ерөнхий
  `JsonStringEnumConverter`-ээр уншигддаг тул JSON integer (`"packageScope": 99`)
  чимээгүй нэвтэрнэ; `PackageScope`-д `Enum.IsDefined` шалгалт алга
  (printColorMode шиг тусгай converter/шалгалттай болгох).
- **F22 (Мэдээлэл):** PFA-ийн writer нь гэрээний класс reference хийлгүй гараар
  бичсэн serializer тул drift нь runtime-д л илэрнэ — канон validator-ийг release
  gate-дээ байлгах нь заавал (одоогоор mos тийм байгаа).
- `SheetSourceApplication.CityGen` гишүүн ямар ч producer-ээс хүрдэггүй
  (зориулалтын нөөц гэж үзэв).

---

## 2. CityGen project-site sidecar (CGA → STU)

CGA sheet package **бичдэггүй** — CGA/CGM/IFG гурвуулаа `.erks-sheets.json`
үйлдвэрлэдэггүй нь батлагдсан. Бодит гэрээ нь өөр:

- Формат: `<dwg-нэр>.erks-citygen-site.json` sidecar, `schema =
  "erks.citygen.project-site"`, `schemaVersion = 1`.
- CGA writer: `ErkSCityGen.AutoCAD.App\ProjectSiteService.cs:383-393`;
  Studio reader: `ErkS.Platform.Core\CityGenProjectSiteManifest.cs`.
- Studio schema/version-ийг **яг таг** шаарддаг (Ordinal + `== 1`), EPSG
  32645–32650 хязгаартай, Polygon шаардлагатай — fail-closed, хоёр тал v1 дээр
  нийцтэй. **Зохицуулалтын дүрэм:** CGA v2 гаргахын өмнө Studio эхэлж
  шинэчлэгдэх ёстой (forward-compat цонх байхгүй).
- DTO хоёр талдаа тусдаа private класс — drift эрсдэл бага ч гэсэн бий
  (валидаци сайтай тул хүлээн зөвшөөрөгдөнө).

---

## 3. Cloud ERA API (STU ↔ SRV)

Сервер: `Erk-S-Server\src\ErkS.LicenseServer`. OpenAPI snapshot хоёр репод
**byte-ижил** (SHA-256 таарсан), 70 үйлдэл. Studio-ийн дуудсан бүх route сервер
дээр амьд — **CRITICAL эвдрэл байхгүй**.

Нийцтэй нь батлагдсан: session/refresh урсгал (15 мин токен, 2 мин margin,
single-flight), ETag/If-Match/If-None-Match/304 бүх хамгаалагдсан route дээр,
chunked upload (8/12MB, X-Chunk-SHA256, resume, идемпотент complete), sheet
comment 6 DTO 1:1, source-package/альбомын DTO-ууд 1:1, relationship-boundary
header нэр + policy version byte-ижил.

**Findings:**

- **F1 (ӨНДӨР, STU, аюулгүй байдал): Bearer токен серверийн өгсөн absolute URL руу
  дагаж явдаг.** `StudioAccountService.cs:1389-1398` (ProfileImageUrl) ба
  `:900-913` (logoUrl): `new Uri(base, path)` нь absolute URL-ийг тэр чигт нь
  авдаг тул сервер (эсвэл эвдэрсэн хариу) `https://attacker/x` өгвөл токен гадагш
  илгээгдэнэ. Чатын asset татагч `:1496-1500` prefix guard-тай — ижил guard-ыг
  энэ 2 цэгт тавих. **Засах тал: STU.**
- **F2 (Дунд, STU):** `dan-organization-registry-import-v1` feature key Studio-д
  огт зарлагдаагүй, `/registry-imports` дуудлагууд (`:802, :818`) capability
  gate-гүй — ДАН тохируулаагүй сервер дээр түүхий 503 харагдана. **STU.**
- **F3 (Дунд, STU):** `StudioCloudApiError`-д `CurrentOrganizationConcurrencyToken`
  талбар алга, `X-ErkS-Current-*` сэргээх header-үүдийг уншдаггүй — 412 бүрт
  илүү round-trip/бүтэн re-list. **STU.**
- **F9 (Дунд, STU, баримт):** `CLOUD-API-CONTRACT.md` ноцтой хоцорсон: 16 key-ийн
  9-ийг л жагсаадаг; sheet comments (5 route), чат (5 route), ДАН import,
  stages/basis-sources/logo route-ууд огт байхгүй; «generated client бүх гадаргуу»
  гэсэн нь бодитод 68-ын 11 (бусад нь гараар); If-Match гэсэн заалт мортально
  body/form token-той 8+ route-д нийцэхгүй; хоёр дахь DTO давхарга
  (`StudioCloudContracts.cs`, ~78 төрөл) байдаг нь «second DTO contract үүсгэхгүй»
  заалттай зөрчилддөг. Бүтэн дахин бичилт хэрэгтэй. **STU.**
- **F10 (Бага, STU):** `organizations`, `collaboration`, `relationship-boundary`,
  `native-source-remains-local` 4 key зарлагдсан ч хэзээ ч Require/Supports-д
  ордоггүй — gate нэмэх эсвэл баримтад «мэдээллийн» гэж тодотгох. **STU.**
- **F11 (Бага, STU):** `RetireSourcePackageAsync` нь Require-ээ
  `EnsureFreshSessionAsync`-аас ӨМНӨ дууддаг (хүйтэн session дээр буруу алдаа);
  `SetAlbumComponentManifestAsync` capability шалгалтгүй. **STU.**
- **F12 (Бага, STU):** Dev-loopback 404 үед capability багц зохиодог fallback
  (`SAS:1806-1840`) нь баримтын «404-өөс feature бүү тааварла» заалттай зөрчилддөг
  — sanctioned exception гэж баримтжуулах эсвэл авах. Product catalog-ийн 404
  fallback мөн адил. **STU.**
- **F16b (Бага, STU, далд):** Generated client `X-ErkS-Relationship-Boundary`-г
  БҮХ хүсэлтэд болзолгүй хавсаргадаг, гар замд зөвхөн хэрэглэгч зөвшөөрснөөр
  явдаг — relationship route generated client-ээр хэзээ нэгэн явбал auto-acknowledge
  болно. Header-ийг opt-in болгох. **STU.**
- **F23 (Мэдээлэл, SRV):** `/session/refresh` нууц үг дахин шалгалгүй
  licenseId+activationId+fingerprint-ээр 15 мин токен дахин олгодог (rate-limit
  60/15мин) — санаатай эсэх шийдвэрийн бичлэг алга. **SRV шийдвэр гаргаж бичих.**
- **F24 (Бага, STU):** `TokenType`/`ServerTimeUtc`/`Scopes`-ийг үл тоож "Bearer"
  hardcode хийдэг; **F25 (Гоо зүй):** `ManifestSchemaVersion` default "1.0"(SRV)
  vs "1"(STU), формат баталгаажуулалтгүй.
- Sheet comments-д capability key, concurrency, retry огт алга (одоогоор whole-list
  семантик тул зөвшөөрөгдөнө; ирээдүйд key нэмэхийг SRV-тай тохирох).

## 4. Update / catalog / бусад SRV сувгууд

- Update гинж бат бөх: HTTPS шаардлага, SHA-256, PE header, WinVerifyTrust
  (WTD_REVOKE_WHOLECHAIN), publisher «Erk-S LLC» exact — бүгд ажиллаж байна.
- **F13 (Бага, STU, баримт):** `UPDATE-SIGNING.md`-д кодод буй **pinned
  untrusted-root exception** (`UpdatePackageSecurityPolicy.cs:150-175`, pinned
  thumbprint) баримтжаагүй; нэмэлт identity/catalog шалгалтууд ч баримтад алга;
  publication-time талбар DTO-д байхгүй. Баримтыг кодтой нийцүүлэх. **STU.**
- **F14 (Бага, STU):** Product catalog (`/api/products/catalog`,
  `/api/installers/latest`) нь updater-ийн `ValidateTransport`-ийг ашигладаггүй
  (non-loopback http зөвшөөрөгдөнө); `StudioSiteImageCache` серверийн өгсөн ямар ч
  absolute URL-аас зураг татна (токен явдаггүй тул эрсдэл бага, гэхдээ хязгаарлах
  нь зөв). **STU.**
- Update/catalog endpoint-ууд OpenAPI snapshot-д байхгүй (Cloud ERA-ийн гадна) —
  F9-ийн дахин бичилтэд «non-Cloud-ERA сувгууд» бүлэг болгож оруулах.

## 5. Гэрээний талбарын семантик (платформ хэмжээний шийдвэрүүд)

- **F18:** `ERKS_STUDIO_PROJECTS_ROOT` env — PFR/PFA уншдаг гэж үздэг, Studio
  дэмждэггүй. ШИЙДВЭР: Studio дэмжинэ (аудитын дараах засвар). **STU.**
- **F19:** `stageId`/`workPackageId` — семантик холимог (Guid "N" vs stage code),
  Studio UI хэзээ ч бөглөдөггүй. Гэрээнд «opaque optional string» гэж тодотгох. **STU (баримт).**
- **F20:** `exportMode` — Studio огт уншдаггүй, гэрээнд байхгүй. «Producer-ийн
  мэдээллийн талбар, consumer үл тоомсорлоно» гэж баримтжуулах. **STU (баримт).**
- **F21 (Бага, STU дотоод):** `WorkingDrawingAlbumFormatFactory.CreateGeometryHash`
  нь гэрээний SHA-256 биш legacy pipe-join (дотоод opaque хэрэглээ тул гэрээ
  зөрчихгүй, цэвэрлэх); каталогийн built-in форматууд `GeometryHash=""` тул
  snapshot↔inline харьцуулалт үргэлж «өөрчлөгдсөн» гардаг.

## 6. ONE (платформ цөм) ба бусад

- Studio код root `core/`/`services/`/`ui/`-г **огт reference хийдэггүй** —
  кодын хамаарал 0. `ErkS.Platform.Core` namespace нь root `core/`-той зөвхөн
  нэрээрээ давхцдаг (түүхэн, санаатай биш) — нэгтгэх/rename шийдвэр платформ
  түвшинд хэлэлцэх асуудал, уялдааны эвдрэл биш.
- Гуравдагч талын хосты (WebView2 газрын зураг): unpkg, OSM/OpenTopo tiles,
  Google/Azure Maps (env key-ээр) — бүгд site-context editor дотор тусгаарлагдсан.
- Токен хадгалалт: access token зөвхөн санах ойд; license/activation id Windows
  Credential Manager; `account.json` нь нууц агуулдаггүй metadata (гэхдээ
  CLOUD-API-CONTRACT-ийн хадгалалтын заалтад дурдаагүй → F9-д багтана).
- 401-д тусгай handling алга (2 минутын proactive refresh-д найддаг) — хүлээн
  зөвшөөрөгдсөн дизайн, гэхдээ refresh бүтэлгүй бол генерик алдаа.

---

## 7. Дүгнэлт ба санал болгох дараалал

| # | Finding | Зэрэг | Засах тал |
|---|---------|-------|-----------|
| F1 | Bearer токен серверийн өгсөн absolute URL руу (2 цэг) | Өндөр | STU |
| F2 | ДАН registry-import capability gate алга | Дунд | STU |
| F3 | 412-ийн сэргээх токен/header уншигддаггүй | Дунд | STU |
| F4 | PFR мэргэжлийн марк (ТХ, хоосон марк, 6 багц хүрэхгүй) | Дунд | PFR (+STU каталог шийдвэр) |
| F5 | PFR ажлын зургийн format геометр зохиомол | Дунд | PFR |
| F6 | PFR шүүсэн олонлогоо FullSnapshot гэдэг | Дунд | PFR |
| F7 | PFR pre-publish шалгалтгүй + A1 fallback quarantine эрсдэл | Дунд | PFR |
| F8 | PFA writer-ийн 5 сул тал (round-trip, tmp, timestamp, trim, docPath) | Бага-Дунд | PFA |
| F9 | CLOUD-API-CONTRACT.md бүтэн дахин бичилт (16 key, 15+ route, DTO давхарга) | Дунд | STU |
| F10-F12, F16b, F24 | Capability gate-ийн жижиг зөрүүнүүд | Бага | STU |
| F13 | UPDATE-SIGNING.md ↔ код (pinned root exception г.м.) | Бага | STU |
| F14 | Catalog transport/image host хязгаарлалт | Бага | STU |
| F16 | packageScope integer-bypass hardening | Бага | STU |
| F17 | ShellView.cs:3458 үхмэл fallback | Бага | STU |
| F18 | ERKS_STUDIO_PROJECTS_ROOT дэмжих | Бага | STU |
| F19-F21 | Гэрээний талбарын баримтжуулалт + дотоод цэвэрлэгээ | Бага | STU |
| F22 | CityGen v1 pin зохицуулалт, PFA drift, CityGen enum | Мэдээлэл | — |
| F23 | Refresh-without-password шийдвэрийн бичлэг | Мэдээлэл | SRV |
| F25 | ManifestSchemaVersion "1.0" vs "1" | Гоо зүй | хамтарч |

Санал болгох эхний ээлж: **F1 → F2 → F3 → F9** (STU), зэрэгцээд PFR-т F4-F7,
PFA-д F8-ыг тус тусын сешнд нь даалгах. F4-ийн каталогийн тал (ТХ маркийг
Studio таних эсэх) продуктын шийдвэр тул Мастерт өргөн мэдүүлэв.
