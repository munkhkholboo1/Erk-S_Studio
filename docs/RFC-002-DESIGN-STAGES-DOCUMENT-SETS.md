# RFC-002: Design Stages And Document Sets

Status: Superseded for the first product scope
Date: 2026-07-11
Scope: Erk-S Studio project workspace, album/document model, Cloud ERA project assignment

The implemented first scope is only `BuildingArchitectureConcept`. Studio now
uses `ProjectWorkspace (.erksproject)` as the root and stores albums as child
`.erksalbum` documents. The active navigation is `Projects`, then inside one
project: `Overview / Foundation / Sources / Albums / Reports / Archive`.
Future stages and work packages in this RFC remain design input and are not
shown as active product structure.

## Purpose

Erk-S Studio must open as a project hub, not as a single album editor. A
project contains legal/design stages, work packages, organizations,
participants, albums, reports, and publishable PDF documents.

This RFC follows the Mongolia design-stage research report:

`C:\Users\munkh\Documents\Erk-S Platform\outputs\mongolia_design_stages_report.md`

## Core Decision

An album is not the project. An album is one controlled PDF output inside a
project stage or work package.

Studio is therefore a project PDF document control app:

- Project hub first.
- Project workspace after a project is opened.
- PDF reader and renderer as the central work surface.
- Publish/review/version actions belong to each album/document, not to a global
  publish page.

## Studio Startup

When Studio opens, it shows global user scope:

- Projects
- My Companies
- License / Account
- Settings

The user should not see project information, source folders, albums, or publish
actions until a project is opened.

## Project Workspace

After a project is opened, Studio switches to project scope:

- Overview
- Project Info
- Stages
- Work Packages
- Participants / Roles
- Albums / PDF Reader
- Documents
- Activity / Audit

The project workspace keeps a way back to the project hub.

## Stage Model

The default stage vocabulary is:

1. `ConceptDesign` - Загвар зураг
2. `SketchDesign` - Эх загвар зураг / эскиз
3. `TechnicalDesign` - Техникийн зураг, optional and mostly for complex projects
4. `WorkingDrawing` - Ажлын зураг
5. `AsBuilt` - Гүйцэтгэлийн зураг

Stages must be versionable/configurable because laws, rules, and required
document sets can change.

## Assignment Rule

The project may have different organizations for different stages or work
packages.

Important example:

- A company may prepare the concept/sketch design.
- The client may choose another company for working drawings.
- A participant may have multiple roles.
- A design organization can manage its own company profile but cannot replace
  the client/server-assigned project organization.

Studio must model assignment at these levels:

- project
- stage
- work package
- album/document

## Work Package Model

Working drawing projects can contain many work packages:

- General plan
- Architecture
- Structure
- Water and sewage
- Heating, ventilation, cooling
- Electrical
- Communication, alarm, automation
- Fire systems
- External engineering networks
- Road, parking, pedestrian path
- Landscape and exterior improvement
- Construction organization
- Budget and quantity

The first implementation can store these as flexible rows, not hard-coded
rules.

## Document And Album Model

Studio document records must support:

- album PDF
- presentation PDF
- explanation note
- drawing list
- report
- uploaded supporting document
- future generated form/document

Each document/album has:

- stage id
- optional work package id
- owner organization id/name
- title
- type
- PDF path or server document id
- version
- status
- publish state
- audit history placeholder

## Publish Rule

There is no global `Publish` workspace as the final design.

Publishing belongs to each controlled output:

- album
- report
- explanation note
- drawing list
- supporting document

Each output may have its own review, approval, version, server location, and
publish history.

## First UI Migration

Prototype navigation must move from:

`Төсөл / Эх үүсвэр / Альбом / Компани / Түгээх`

to:

Global scope:

`Projects / My Companies / Account / Settings`

Project scope:

`Overview / Project Info / Stages / Work Packages / Participants / Albums / Documents / Activity`

Existing functionality is kept but moved into the correct context.

## First Model Migration

Keep `.erksalbum` backward compatible. Add optional fields only:

- `Stages`
- `WorkPackages`
- `Documents`

Old files with no new fields must load as local/simple album projects.

## Open Decisions

- Official stage codes and Mongolian labels.
- Project type templates and document set versions.
- Exact API contract for server-assigned organizations per stage/package.
- Whether a local draft may create stages before server binding.
- First production template: concept/sketch-only or simple working drawing.
