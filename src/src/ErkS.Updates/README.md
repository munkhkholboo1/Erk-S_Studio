# ErkS.Updates — нэг шинэчлэлийн сан (CGA, CGM, STU, PFA, PFR)

Гэрээ: `E:/Erk-S Platform/_shared/delta-update-contract-2026-10-26.md`. Эзэн, 2026-10-26: «зөвхөн өөрчлөгдсөн
хэсгүүдийг нь update хийдэг болгуулаарай». Бүтээгдэхүүн бүр энэ санг ашиглана, өөрөө дахин бичихгүй.

- Сан: `src/ErkS.Updates` (net48, net8.0, net10.0). Revit/AutoCAD net48 нь net48-ийг, net8+ нь net8-ийг авна.
- Хэрэглэгч: `src/ErkS.Updates.Apply` (net48 консол). Хост хаагдсаны дараа түр хуулбараас ажиллаж staging-ийг суулгалтад
  хэрэглэнэ, амжилтгүй бол хуучныг бүтнээр нь буцаана, дараа нь программуудыг дахин эхлүүлнэ.

## Бүтээгдэхүүний урсгал

```csharp
var downloader = new DeltaDownloader(http, new Uri("https://erk-s.mn"), "<PRODUCT CODE>");
UpdateManifest? target = await downloader.GetManifestAsync(version: latest.Version, channel: null, ct);
if (target is null) { /* сервер манифестгүй: хуучин бүтэн суулгагч */ }

UpdateManifest installed = InstallManifest.LoadOrScan(installDir, "<PRODUCT CODE>", currentVersion);
UpdatePlan plan = UpdatePlanner.Plan(installed, target, fullInstallerBytes: installerSize); // 0 = мэдэгдэхгүй
if (plan.IsEmpty || plan.UseFullInstaller) { /* бүтэн суулгагч (>60%) */ }

await downloader.DownloadAsync(plan, staging, new Progress<UpdateProgress>(p => ui.Show(p.Text)), ct); // %, МБ, МБ/с
File.WriteAllText(Path.Combine(staging, "target.json"), target.ToJson());

LiveApplyResult live = UpdateApplier.ApplyLive(installDir, staging, plan); // 5a: хаалгүй
if (live.Applied.Count > 0) ReloadRequested(live.Applied);               // өөрийн тохиргоо/загвар/дүрсийг дахин уншина
if (plan.NeedsRestart || live.Deferred.Count > 0)
{
    // «хаагаад суулгах уу?» — хэрэглэгчийн зургийг хэзээ ч өөрөө хадгалахгүй, хүчээр хаахгүй.
    // installDir\updater\*-ийг %TEMP%-д хуулж:
    // ErkS.Updates.Apply.exe --install <dir> --staging <dir> --wait <host pid> [--close ErkS.Sync] [--launch <exe>]...
}
```

- `InstallManifest.FileName` = `.erks-install.json` — суулгагч бичнэ (нийтлэх скрипт publish хавтсанд үүсгэж payload-д хийнэ);
  байхгүй бол эхний удаа хэшлэж үүсгэнэ. `TryReadVersion(dir)` — суулгасан хувилбар (live шинэчлэл exe солихгүйгээр хувилбар ахиулна).
- Гарын үсэг: манифест Erk-S сертификатаар (`ManifestSignature.ErkSThumbprint` = 52B2508A…) — эхлээд pin, дараа нь body
  байтын гарын үсэг, дараа нь л body. Файл бүр манифестын SHA-256-тай тулгагдана; `ErkS.*` exe/dll Authenticode-тай бол pin.
- Татах: `{filesUrl}/{sha256}`, тасарвал `.part`-аас Range-ээр үргэлжилнэ, аль хэдийн татсаныг алгасна, 4 удаа давтана.
- Хэрэглэх: солигдох/хасагдах файлыг `.rollback`-д зөөж, шинийг `*.erks-new`-ээр тавина; суулгалтын манифест хамгийн сүүлд.
  Зөвхөн манифест нэрлэсэн замд хүрнэ — хэрэглэгчийн файлд хүрэхгүй.

## Нийтлэх (SRV)

`ErkS.LicenseServer.exe --publish-files --dir <суулгалтын бүтэц> --version v0.001.NN --product-code <CODE> [--live "<glob>;<glob>"]`
(`ERKS_LICENSE_SERVER_DATA` = серверийн data root). `--live` таарахгүй файл бүр `restart`. Studio-гийн жишээ:
`scripts/Publish-Studio-DemoToServer.ps1` (delta алдаа гарвал анхааруулаад бүтэн суулгагч хэвээр).
