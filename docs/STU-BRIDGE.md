# STU-гийн гүүр — Studio-г гаднаас удирдах нэг оролт

**Эзний дүрэм (2026-10-20):** «цаашид powershell эсвэл cmd ашиглаад студиог гаднаас удирдах боломжтой болгож
ком удирдалгүй гаднаас нь гүүрээр ажилдаг болгоорой». Агентууд Studio-г computer-use (дэлгэц товших, хулгана)-ээр
БИШ, зөвхөн энэ гүүрээр удирдана.

## Хэрэглэх

```bash
powershell -NoProfile -ExecutionPolicy Bypass -File "E:/Erk-S Platform/Erk-S Studio/tools/stu.ps1" status
```

cmd-ээс: `"E:\Erk-S Platform\Erk-S Studio\tools\stu.cmd" status`

| Команд | Юу хийх |
|---|---|
| `list` | Ажиллаж буй Studio-ууд (pid, pipe) |
| `actions` | Гүүрийн бүх үйлдэл, аргумент |
| `status` | Нээлттэй төсөл, эх үүсвэр (зориулалт, сүүлийн багц), альбум (`albumPages` = альбумын бичлэг, `albumPdfPages` = PDF-ийн бодит хуудас; нэг бичлэг олон хуудас болж болно), cloud төлөв, ажиллаж буй үйлдэл |
| `notices` | Гүүрийн үед гарах байсан мэдэгдлүүд |
| `run <үйлдэл> -Arg key=value …` | Үйлдэл ажиллуулах |

Үйлдлүүд (Studio-гийн товчтой ЯГ нэг функцийг дуудна):

| Үйлдэл | Товч | Аргумент |
|---|---|---|
| `projects` | Төслийн жагсаалт | — |
| `open-project` | Төслийн карт дарах | `path=<хавтас/.erksproject>` эсвэл `code=<код>` |
| `check-sources` | «Эх үүсвэр шалгах» | — |
| `rebuild-album` | Альбум шинэчлэх | — |
| `collab-status` | (унших) CGA хамтын файл: бичигч бүрийн лог, хүлээгдэж буй, дутуу дугаар | — |
| `sync-central` | «Хамтын ажиллагааг асаах/унтраах» (Erk-S Central) | `path=<central .rvt> on=true\|false` |
| `album-format` | «Нийтлэг хуудасны формат» сонгогч | `columns=1-4 rows=1-4`; аргументгүй бол одоогийн формат (сонгосон эсэх) |
| `rearm-component` | Cloud-д алга болсон, локал угсралт зурдаг хэсгийг дахин илгээхээр тэмдэглэх | `code=<canonical код>` |
| `sheet-numbers` | (унших) хуудас бүрийн эх дугаар ба альбумд хэвлэгдэх дугаар («БА-8») — эшлэлийн гэрээ `_shared/sheet-reference-contract-2026-10-20.md` | — |
| `components` | (унших) синкийн хэсгүүд: локал угсралт, Cloud, хүлээгдэж буй код | — |
| `sync-preview` | Cloud товч — төлөвлөгөөг харах, илгээхгүй | — |
| `sync` | Cloud товч + «Sync хийх» | `confirm=true` |
| `relink-source` | «Эх файлыг солих» | `source=<id/нэр> path=<файл>` |
| `import-front-matter` | «ЕХ мэдээлэл» › ноорог оруулах + хадгалах | `file=<.md>` |
| `sync-status` | (унших) Erk-S Sync: tray ажиллаж буй эсэх, серверийн төлөв, хавтас бүрийн файлын тоо, холбогдсон машин, зөрүүтэй файлын тоо — файлын нэргүй | — |
| `sync-folders` | (унших) Таны багийн төслүүдийн Erk-S Sync хавтас (сервер), энэ дискэд тэмдэглэгдсэн эсэх | — |
| `sync-make` | «Erk-S Sync фолдер болгох» | `path=<хавтас>` `[project=<код>]` — project-гүй бол Studio-д хүн сонгоно |
| `sync-join` | «Erk-S Sync фолдерт холбох» | `path=<хавтас>` `[folder=<folderId>]` — folder-гүй бол хүн сонгоно |
| `sync-unlink` | «Холболт салгах» (файлууд хэвээр) | `path=<хавтас>` |
| `sync-decide` | Зөрүүтэй файлын шийдвэр: `theirs` — нөгөө машины хувилбарыг авна (энэ машиных `.erks-synceplaced`-д), `mine` — энэ машиных үлдэж нөгөө нь авна | `folder=<id> choice=theirs\|mine [peer=<түлхүүр>] [path=<файл>]` |
| `sync-unregister` | «Синк хавтсыг цуцлах»: төслийн синк хавтсын бүртгэлийг бүх багт цуцална (файлууд хэвээр) | `project=<төслийн код>` |
| `close` | Studio хаах | — |

Хариу нь JSON: `ok`, `reasonCode` (татгалзсан шалтгаан), `message` (Studio-гийн статусын мөр), `result`,
`notices`. Амжилттай бол exit 0, үгүй бол 1.

## Дүрэм

1. **Нэг оролт.** Studio-д computer-use, SendKeys, UI Automation, Win32 мессеж, `project.erksproject`-д шууд бичих
   хориотой (Studio ажиллаж байхад). Үйлдэл дутвал «STU агент» гүүрт нэмнэ (`StudioBridgeActionCatalog` +
   `ShellView.Bridge.cs`) — тойрч гарахгүй.
2. **Товчтой ижил функц.** Гүүрийн үйлдэл бүр товчны дууддаг функцийг дуудна; шинэ логик гүүрт бичихгүй. Товч
   файл сонгох цонх нээдэг бол цонх нь зөвхөн файлыг сонгодог, ажлыг тусдаа функц хийдэг байхаар хуваана.
3. **Чимээгүй горим.** Гүүрийн үйлдлийн үед (дууссанаас хойш 15 с) Studio-гийн мэдэгдлийн цонх хүн хүлээхгүй:
   OK эсвэл аюулгүй хариу (Үгүй/Болих) өгөгдөж, текст нь `notices`-д ирнэ.
4. **Синк.** `sync` нь Cloud руу нийтэлнэ — эхлээд `sync-preview`-ээр төлөвлөгөөг харж, `confirm=true`-тэй л
   ажиллуулна.
5. **Аюулгүй байдал.** Pipe `\\.\pipe\ErkS.Studio.Bridge.<pid>` зөвхөн Studio-г ажиллуулж буй Windows хэрэглэгчид
   нээлттэй; сүлжээгээр хүрэхгүй. Олон Studio ажиллаж байвал `-StudioPid`.
6. **Studio угсрах.** commit-if-green `ErkS.Studio.exe`-г угсардаггүй — гүүрийн шинэ үйлдлийг ашиглахын өмнө host-ыг
   build хийж Studio-г дахин нээнэ.

## Erk-S Sync (SYN)

`ErkS.Sync.exe` нь Studio-тэй хамт суух ар талын (tray) процесс: Windows нэвтрэхэд эхэлнэ (HKCU Run «ErkS.Sync»),
Explorer-ийн баруун товчны «Erk-S Sync» цэс (HKCU `Software\Classes\Directory\shell\ErkS.Sync`), pipe
`\.\pipe\ErkS.Sync.<SID>`. Түлхүүрүүдийг `ErkS.Sync.exe --register` / `--unregister` л бичиж арилгана.
«Фолдер болгох», «холбох» нь хүний шийдвэр тул Studio (Cloud ERA эрх)-оор явна; tray нь төхөөрөмжийн
токеноор индекс, LAN солилцоо хийнэ. Төлөв: `%LOCALAPPDATA%\Erk-S Sync\state.json` (`sync-status`).
Twin-д: `sync-status`-тэй ЯГ ижил JSON-ыг (`SyncStatusReport`, зөвхөн тоо) tray pass бүрд (≤60 с)
`%LOCALAPPDATA%\Erk-S Platform\Sync\status.json`-д атомаар бичнэ (tmp → replace; гарахдаа `running:false`),
Studio хаалттай ч уншигдана. `ERKS_SYNC_HOME`-тэй хоёр дахь instance энэ файлыг дарахгүй.
SYN 1.2 (LAN дамжуулалт): хавтас бүрт `conflictCount` (шийдвэр хүлээж буй зөрчлийн хуулбар), peer бүрт
`receivedCount`/`keptBothCount`/`bytesReceived`/`lastReceivedAtUtc` (сүүлийн хүлээн авалт), `waitingCount` (нээлттэй
тул хүлээгдэж буй), `unrelatedCount` (түүхгүй зөрүү — хүн шийднэ). Шинэ хувилбар зөвхөн өмнөхөө мэддэг үед солигдоно,
хоёр талын засвар «Нэр (Erk-S зөрчил <төхөөрөмж> <огноо>).ext» хуулбараар хадгалагдана, Revit central дамжихгүй.
