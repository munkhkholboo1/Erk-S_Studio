# Erk-S Platform Studio

Erk-S Platform-ийн үндсэн бие даасан программ.

**Нэг өгүүлбэрээр:** Cloud ERA болон local төслүүдийг нээж, төслийн суурь
мэдээлэлд тулгуурлан AutoCAD, Revit, CityGen зэрэг эх үүсвэрийн PDF хуудсаас
альбум, тайлан боловсруулдаг project-centric программ.

## Гол зарчим

- **Алдагдалгүй хүлээн авалт.** Эх программууд хуудас бүрээ vector PDF + JSON
  manifest ("sheet package") болгон экспортолно. PDF бүр SHA-256 хэштэй тул
  дамжуулалтын явцад өөрчлөгдсөн/дутуу файл шууд илэрнэ.
- **Нэг эх сурвалж.** Компанийн мэдээлэл, булангийн хүснэгтийн агуулга энд
  төвлөрнө; AutoCAD/Revit plugin-ууд цаашид булангийн хүснэгт зурахаа болино.
- **Бодит цаг.** Эх үүсвэрийн фолдерт шинэ багц ирмэгц альбом автоматаар дахин
  угсрагдана; түгээгдсэн альбом серверээр оролцогчдод шинэчлэгдэнэ.

## Layout

- `src/ErkS.Studio.slnx` — solution.
- `src/src/ErkS.Platform.Contracts` — sheet package manifest схем + унших/бичих + hash баталгаажуулалт.
- `src/src/ErkS.Platform.Core` — `ProjectWorkspace` root, суурь мэдээлэл, project-owned source, album/report/archive index, sheet library, intake, album builder.
- `src/src/ErkS.Platform.Pdf` — PDF угсрагч (нүүр + гарчиг + хуудсуудын vector merge, PDFsharp).
- `src/src/ErkS.Platform.Publishing` — сервер лүү альбом илгээх клиент (endpoint серверт нэмэгдэх хүртэл contract тодорхойлно).
- `src/src/ErkS.Studio` — **нимгэн host exe**: цонхны хүрээ, DevUpdate engine, policy fallback.
- `src/src/ErkS.Studio.Contracts` — host ба App модулийн хоорондох жижиг гэрээ (`IStudioAppModule`).
- `src/src/ErkS.Studio.App` — **hot-reload модуль**: нүүрэнд зөвхөн төслийн каталог; төсөл нээсний дараа Ерөнхий / Суурь / Эх үүсвэр / Альбум / Тайлан / Архив.
- `src/tools/ErkS.Platform.SamplePackage` — AutoCAD/Revit-гүйгээр туршилтын багц үүсгэгч.
- `src/tests/ErkS.Platform.Core.Tests` — xUnit тестүүд (manifest round-trip, hash tamper илрүүлэлт, album compose).

## DevUpdate — рестартгүй хөгжүүлэлт

CityGen-ий AutoCAD DevMod загвартай ижил: host процесс амьд үлдэж, App модуль
шинэ AssemblyLoadContext-д shadow copy-гоос ачаалагдана (файлын түгжээ үүсэхгүй).
Зөвхөн `ErkS.Studio.Contracts` host context-д тогтвортой үлдэнэ. App, Contracts,
Core, PDF, Publishing assembly-ууд нэг collectible context-д хамт ачаалагдаж,
DevUpdate бүрээр нэг хувилбар болж бүхлээрээ солигдоно.

- Программ ажиллаж байхад **Ctrl+U** (эсвэл дээд devbar-ын DevUpdate товч) —
  App модулийг эх кодоос нь `dotnet build` хийгээд рестартгүйгээр солино.
- **Auto** checkbox — `dotnet build src/src/ErkS.Studio.App` гараар
  (эсвэл `dotnet watch build`-ээр) хийх бүрт программ өөрөө шинэчлэгдэнэ.
- Билд бүр `builds/devmod/app` фолдерыг шинэлдэг; host түүнийг ажиглана.
- Dev горим нь host-ийн хажуудах `ErkS.Studio.devroot` marker-аар танигдана;
  source build/publish marker-ийг автоматаар бичнэ.
- Windows Application Control шинэ managed DLL-ийг хоригловол bundled static
  module руу fallback хийнэ. Дараагийн DevUpdate single-file host publish хийж
  шинэ process руу хяналттай шилжинэ.

Мөн адил: AutoCAD/Revit plugin-ууд sheet package-аа фолдерт бичдэг тул Studio
шинэчлэгдэхэд AutoCAD/Revit-ийг рестарт хийх шаардлага огт гарахгүй.

## Ажиллуулах

```powershell
cd src
dotnet run --project src/ErkS.Studio
```

Smart App Control идэвхтэй хөгжүүлэлтийн машинд single-file host:

```powershell
dotnet publish src/ErkS.Studio/ErkS.Studio.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -o ../builds/release/win-x64
```

Туршилтын багц үүсгэх (өөр terminal):

```powershell
dotnet run --project tools/ErkS.Platform.SamplePackage -- C:\temp\erks-drop 4
```

Төслөө нээгээд "Эх үүсвэр" дотор Revit/AutoCAD source нэмэхэд project-owned
`sources/<source>/deliveries` хавтас автоматаар үүснэ. Sample package-ийг тэр
delivery хавтас руу бичихэд хуудсууд харагдаж, "Альбум" дотроос PDF угсарна.

## Project storage

```text
Studio Projects/<project-code>/
├── project.erksproject
├── sources/<source>/deliveries/
├── albums/building-architecture-concept.erksalbum
├── reports/
└── archive/
```

Хуучин project-container `.erksalbum` файлыг нээхэд эх файлыг өөрчлөхгүйгээр
шинэ `project.erksproject` болон project-owned album document үүсгэнэ.

## Тест

```powershell
cd src
dotnet test tests/ErkS.Platform.Core.Tests
```

## Дараагийн алхмууд

1. AutoCAD Platform plugin: Layout бүрийг vector PDF болгон хэвлэж sheet package бичдэг экспорт нэмэх.
2. Revit Platform plugin: мөн адил sheets экспорт (одоогийн AlbumSheetManager-тай уялдуулах).
3. Булангийн хүснэгтийн загвар + компанийн мэдээллийг PDF дээр давхарлан зурах (plugin-ууд corner table зурахаа болих шилжилт).
4. Erk-S-Server: `/api/platform/albums` endpoint + оролцогчдод бодит цагийн түгээлт (сайтын төсөл хуудас).
5. Альбомын бүлэг (section) удирдлага: хуудсуудыг чирж эрэмбэлэх UI.
