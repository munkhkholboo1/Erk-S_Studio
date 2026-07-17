# Erk-S Platform Studio — Architecture

Cloud ERA суурь boundary, project/role/document ownership, Sheet Format Engine,
болон AutoCAD/Revit exporter-ийн шинэ үүргийг
[`RFC-001-STUDIO-CLOUD-FOUNDATION.md`](RFC-001-STUDIO-CLOUD-FOUNDATION.md)-д
тогтоосон. Энэ architecture нь project workspace → source delivery →
album/report pipeline-ийг тайлбарлана; RFC-001 нь model/API/renderer
хэрэгжилтийн үндсэн гэрээ болно.

## Project ownership

```text
ProjectWorkspace (.erksproject)
├── Foundation
│   ├── InitiationBasis
│   ├── PlanningTask (АТД)
│   └── DesignCompany (stage-scoped snapshot)
├── Sources
├── Deliverables
│   ├── Albums (.erksalbum)
│   └── Reports
└── Archive
```

Studio root дээр зөвхөн project catalog байна. Source, album, report болон PDF
нь заавал нээлттэй project-ийн child байна. Cloud/Local нь project type биш,
origin/sync status юм.

## Data flow

```
AutoCAD (Erk-S Platform plugin)          Revit (Erk-S Platform plugin)
  Layout бүрийг vector PDF болгон          Sheet бүрийг vector PDF болгон
  хэвлэж, manifest бичнэ                    хэвлэж, manifest бичнэ
        │                                        │
        ▼                                        ▼
 project-owned delivery folder  ◄──────  бусад эх үүсвэр (гар PDF, CityGen...)
        │
        │  *.erks-sheets.json (SHA-256 per PDF)
        ▼
  ErkS.Platform.Core.SheetIntakeService (FileSystemWatcher, бодит цаг)
        │  hash verify → lossless / issues
        ▼
  SheetLibrary (сүүлийн экспорт нь ялна, key = app|docPath|sheetId)
        │
        ▼
  AlbumBuilder → AlbumBuildRequest (sections, дараалал)
        │
        ▼
  ErkS.Platform.Pdf.PdfSharpAlbumWriter
        │  нүүр + гарчиг + vector merge (+ дараа нь: булангийн хүснэгт overlay)
        ▼
  album.pdf ──► ErkS.Platform.Publishing.AlbumPublishClient ──► erk-s.mn
                                        (оролцогчид бодит цагт үзнэ)
```

## Sheet package format (Contracts, schema v1)

Багц = нэг фолдер доторх PDF-үүд + `<нэр>.erks-sheets.json` manifest:

```json
{
  "schemaVersion": 1,
  "packageId": "guid",
  "source": {
    "application": "AutoCad | Revit | Manual",
    "applicationVersion": "AutoCAD 2026",
    "documentPath": "C:\\...\\file.dwg",
    "documentTitle": "file",
    "projectCode": "ERKS-2026-014"
  },
  "exportedAtUtc": "2026-07-09T04:00:00Z",
  "sheets": [
    {
      "sheetId": "LAYOUT-01",
      "number": "AR-01",
      "name": "Ерөнхий төлөвлөгөө",
      "discipline": "AR",
      "revision": "0",
      "widthMm": 420, "heightMm": 297,
      "pdfFileName": "sheet-01.pdf",
      "sha256": "...",
      "pageCount": 1
    }
  ]
}
```

Дүрэм:
- Manifest-ийг PDF-үүдийн **дараа** бичнэ (watcher manifest дээр асдаг тул
  бүрэн бус багц хэзээ ч уншигдахгүй).
- Нэг хуудасны дахин экспорт нь өмнөхөө орлоно (`exportedAtUtc` шинэ нь ялна).
- Хэш таарахгүй бол багц "алдаатай" гэж тэмдэглэгдэнэ — чимээгүй алгасахгүй.

## Модулиудын хамаарал

```
Contracts ◄── Core ◄── Pdf
                ▲        ▲
                │        │
            Publishing   │
                ▲        │
                └── Studio (WPF) ── SamplePackage (tool)
```

- Core нь UI/PDF-ээс хамаарахгүй; `IAlbumPdfWriter` interface-ээр Pdf давхаргыг
  урвуу хамааралтай холбоно.
- Studio нь бүх давхаргыг угсарч WPF дээр харуулна; theme нь CityGen-тэй ижил
  Erk-S dark style (StudioTheme).

## Ирээдүйн чиглэл

- **Булангийн хүснэгт**: CompanyProfile + project мэдээллээс PDF overlay-гээр
  зурна (plugin-ууд corner table-ээ хасна). Overlay нь XGraphics.FromPdfPage-ээр
  import хийсэн хуудсан дээр нэмж зурах хэлбэрээр хийгдэнэ.
- **Сервер түгээлт**: Erk-S-Server дээр `/api/platform/albums` (upload) +
  project viewer хуудас; оролцогчид album-ийн шинэ хувилбарыг бодит цагт үзнэ.
- **Live push**: drop folder-оос гадна named pipe/HTTP push (хуучин ErkS_Bridge
  протоколын туршлагыг ашиглана) — файлын систем нь үндсэн найдвартай суваг
  хэвээр үлдэнэ.
