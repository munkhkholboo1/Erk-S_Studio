# RFC-001: Studio Cloud Foundation

Status: Draft foundation
Date: 2026-07-09
Scope: Erk-S Studio, erk-s.mn project workspace, AutoCAD/Revit exporters

## Purpose

Erk-S Studio is not only an album merger. It is the project document control
center for Erk-S Cloud ERA. Studio and the website must create and manage the
same canonical project, while AutoCAD and Revit become source authoring tools
that export clean drawing output and metadata.

This RFC defines the first boundary before implementation:

- Project, company, participant, role, document set, sheet format, album,
  cover, drawing list, explanation note, and publish state belong to Studio and
  the cloud project.
- AutoCAD and Revit do not own project information, company information,
  title blocks, cover pages, drawing lists, explanation notes, or final album
  composition.
- DWG, RVT, and other native source files are not transferred through Studio.
  They remain at their original source. Studio receives only controlled output
  documents, PDF sheets, reports, manifests, hashes, and source references.

## Product Boundary

| Area | Owner | Rule |
| --- | --- | --- |
| Project creation | Website and Studio | Both call the same backend contract and produce the same server project. |
| License and identity | Studio + Server | Studio has License Manager and signs in with the user's account. |
| Project information | Studio + Server | Name, address, type, phase, client, design organization, participants, document set. |
| Company information | Studio + Server | Legal/profile/contact/license/logo/director data. |
| Participants and roles | Studio + Server | One participant can have multiple roles; permissions are the union of assigned roles. |
| Source drawings | AutoCAD/Revit/local source | Native files stay at source; Studio stores only references and exported documents. |
| Sheet PDF export | AutoCAD/Revit | Export vector PDF plus manifest and hash. No title block drawing ownership. |
| Sheet format rendering | Studio | Official frame, grid marks, title block, sheet name area, cover, list, notes. |
| Publishing | Studio + Server | Studio assembles and publishes controlled project documents to the website. |

## Project Creation Rule

There are two project entry points:

1. Website: user signs in to `erk-s.mn` and creates a project.
2. Studio: user signs in through License Manager and creates a project.

Both entry points must create the same result:

- same canonical `projectId`
- same project type/stage/template selection
- same default role set
- same default document set
- same default sheet format rules
- same permission model
- same server-side audit trail

Studio may support local draft work, but a real project must bind to a server
project before collaboration, publishing, member management, or site sync.

## Participant And Role Model

Roles are not a single enum attached to a user. A project participant may have
many role assignments.

Core entities:

- `ProjectParticipant`: user or invited person participating in a project.
- `Organization`: design organization, contractor, client, reviewer, authority,
  consultant, or other project-side organization.
- `Role`: named responsibility such as Admin, Major architect, Architect,
  Constructor, Major Engineer, Engineer, Төсөвчин, Эдийн засагч, Нарийн бичиг,
  Техникч, Client representative, Reviewer, Controller.
- `Permission`: atomic action such as manage members, edit project info,
  approve documents, upload packages, publish album, view financial documents.
- `RoleAssignment`: participant + role + organization/side + optional scope.

Important rules:

- Admin can add, remove, invite, and deactivate project members.
- One participant can have multiple roles, especially on the design/contractor
  side.
- Permissions are calculated from all active role assignments.
- Client-side and reviewer/control-side roles are separate from design-side
  roles even when names look similar.
- Role labels shown on a title block or approval row are output labels, not the
  entire permission system.

## Project Document Model

Studio manages every controlled project document, not only drawing albums.
Required documents differ by project type, phase, and work scope.

Core entities:

- `ProjectTypeTemplate`: the type of project or work package.
- `ProjectStageTemplate`: sketch design, working drawing, or other stage.
- `DocumentSetTemplate`: required albums, reports, notes, approvals, and
  supporting documents for a type/stage.
- `Deliverable`: one required output item in the project.
- `Album`: ordered drawing output assembled from exported sheets.
- `DocumentRecord`: controlled document with version, status, source, hash,
  author, reviewer, and publish state.
- `ReportRecord`: generated or uploaded report such as explanation note.

Start with the simplest templates:

1. Sheet Album Project: exported sheets are collected into one album.
2. Simple Design Documentation Project: album + drawing list + explanation
   note + basic project/company pages.
3. Building Design Project: stage-specific drawing/document/report sets with
   discipline and approval workflow.

## Native Source File Rule

Studio must never become a DWG/RVT vault in the first Cloud ERA foundation.

Allowed through Studio:

- vector PDF sheet output
- generated PDF documents
- uploaded official documents
- JSON manifest
- SHA-256 hash
- source application name/version
- source document title
- source document path hash or external reference
- source sheet id/layout id
- exported timestamp
- revision/status metadata

Not allowed through Studio:

- DWG source file
- RVT source file
- large native authoring file as the normal collaboration format
- source-side family/titleblock files as the official output owner

## Sheet Format Engine

Erk-S has its own sheet format rules. A3 and A4 base sheets are used to produce
different formats, and working drawings and sketch drawings can have different
layouts.

Studio owns the official sheet format. AutoCAD/Revit only need enough spatial
metadata so Studio can recognize the sheet and validate the clean drawing area.

Studio format template:

```json
{
  "formatId": "ERKS-WD-A3-L-01",
  "stageType": "WorkingDrawing",
  "basePaper": "A3",
  "widthMm": 420,
  "heightMm": 297,
  "orientation": "Landscape",
  "drawingContentBox": { "x": 20, "y": 25, "w": 340, "h": 252 },
  "cornerTableBox": { "x": 360, "y": 15, "w": 55, "h": 120 },
  "sheetNameBox": { "x": 20, "y": 15, "w": 340, "h": 10 },
  "gridZone": { "enabled": true },
  "rendererTemplate": "ERKS-WD-CORNER-A3-V1"
}
```

Rules:

- Coordinates are in millimeters.
- The coordinate origin must be fixed per renderer and documented.
- `drawingContentBox` is the clean source drawing area.
- `cornerTableBox`, `sheetNameBox`, grid marks, border, approvals, and title
  block content are rendered by Studio.
- AutoCAD/Revit should not draw official frames, title blocks, drawing lists,
  cover pages, or explanation notes.
- Exporters may use blank/non-plot guide layouts only to help the designer
  place the drawing correctly.

## Sheet Package Contract Direction

Current schema v1 already supports PDF, source, sheet number/name, size, hash,
and project code. The next schema must carry Studio-owned format identity and
source placement metadata.

Required additions for schema v2:

```json
{
  "projectId": "server-guid-or-empty-draft-id",
  "projectCode": "ERKS-2026-014",
  "source": {
    "application": "Revit",
    "applicationVersion": "Revit 2026",
    "documentTitle": "A-Building",
    "documentPathHash": "sha256-of-local-path-or-stable-source-ref"
  },
  "sheets": [
    {
      "sheetId": "source-stable-id",
      "number": "BA-101",
      "name": "1-р давхрын байгуулалт",
      "discipline": "BA",
      "revision": "0",
      "formatId": "ERKS-WD-A3-L-01",
      "widthMm": 420,
      "heightMm": 297,
      "drawingContentBox": { "x": 20, "y": 25, "w": 340, "h": 252 },
      "pdfFileName": "BA-101.pdf",
      "sha256": "..."
    }
  ]
}
```

Validation in Studio:

- manifest hash matches PDF
- page size matches `formatId`
- source drawing sits within the expected clean drawing area
- required sheet metadata is present
- project/stage/document set allows this sheet type
- duplicate source sheets are resolved by latest export/revision rule

## Rendering Pipeline

1. AutoCAD/Revit exports clean vector sheet PDF plus manifest.
2. Studio intake verifies manifest and hashes.
3. Studio identifies project, format, stage, discipline, sheet type, and source.
4. Studio validates page size and drawing content area.
5. Studio overlays official format elements:
   - border/frame
   - grid marks
   - corner table
   - sheet name/number area
   - approval/signature rows
   - company/project/client data
6. Studio generates project-level documents:
   - cover page
   - drawing list
   - explanation note
   - reports required by project type/stage
7. Studio assembles a controlled album/document package.
8. Studio publishes the result to the website and records version/audit data.

## Migration From Revit Platform

The existing Revit implementation is the migration reference, not the future
owner.

Move these responsibilities to Studio:

- `ProjectInfoWindow` and `ProjectInfoCommand` fields become `ProjectProfile`.
- `CompanyManager` and `CompanyLibraryWindow` fields become
  `OrganizationProfile` / `CompanyProfile`.
- `RolePresetService` and approval row fields become participant/role/approval
  output mappings.
- `AlbumSheetService` and `AlbumSheetTemplateProvider` become Studio document
  set templates and sheet records.
- `CompanyCornerTableGenerator` and `BlueprintCoverTitleBlockService` become
  Studio PDF renderer templates.
- `RevitAgentNativeActions` JSON ideas become exporter/Studio manifest
  contract input, with ownership reversed toward Studio.

Do not delete the Revit logic until Studio has accepted replacement contracts
and exporters are updated.

## First Implementation Order

1. Freeze this RFC as the initial boundary.
2. Add Studio domain models for project, organization, participant, role,
   document set, and sheet format template.
3. Add schema v2 draft for sheet package manifest.
4. Add tests for manifest round-trip and sheet format validation.
5. Add a minimal Sheet Format Engine that can load templates and validate PDF
   page size/content boxes.
6. Add Studio UI pages for Project Info, Organizations, Roles, and Sheet
   Formats.
7. Add License Manager sign-in boundary and project binding state.
8. Update Revit/AutoCAD exporters to output clean sheet PDFs plus v2 manifests.
9. Add Studio renderers for corner table, cover page, drawing list, and
   explanation note.
10. Add website `/project/` workspace and backend APIs used by both Studio and
    the site.

## Open Decisions

- Exact coordinate origin for sheet rendering: bottom-left like CAD, or
  top-left like PDF/UI.
- Official list of A3/A4-derived Erk-S formats and naming codes.
- Working drawing versus sketch drawing template differences.
- Minimal first project type to implement in production.
- Server API shape for project creation, role assignment, and document publish.
- Whether Studio allows offline local draft projects before license sign-in.
