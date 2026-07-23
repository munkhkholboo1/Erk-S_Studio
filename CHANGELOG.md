# Version History

This file records product milestones from the first Git source baseline onward.
Older implementation work predates this repository and is not represented as fabricated commits.

## [Unreleased]

- Continue the Cloud ERA project, document, album, and collaboration workflows.
- Continue Revit, AutoCAD, and CityGen source-package integration.

## [0.1.0-dev.12] - 2026-07-23

- Add project-member chat in Studio using the same Cloud ERA message, emoji, and attachment contract as the website.
- Add location markers, measured paths, and independently editable concentric-radius annotations to the location scheme and surrounding-context map.
- Preserve the exact map viewport and high-resolution capture composition between editing, album preview, PDF generation, reopening, and CityGen geometry refreshes.
- Keep annotation selection as editor-only state so saved albums never retain an active radius or marker highlight.
- Migrate legacy generated album pages to the current renderer without discarding project-scoped source data.

## [0.1.0-dev.11] - 2026-07-22

- Reject update catalog entries and downloaded installers that belong to another Erk-S product.
- Verify the Studio product identity and exact release version after Authenticode and SHA-256 validation.
- Keep the website, installer, updater, and update-history version metadata on one Studio release stream.
- Prevent browsers and intermediate proxies from retaining stale product and release-history HTML.

## [0.1.0-dev.10] - 2026-07-22

- Merge independently authored Cloud ERA source manifests and album components without deleting another member's contribution.
- Preserve source ownership and project identity while reconciling same-named sources from different projects and devices.
- Compose shared component PDFs into one canonical album and remove obsolete temporary merge artifacts.
- Add the location scheme and surrounding-context map editor foundation with project-scoped assets.
- Improve source refresh, Cloud dirty detection, project opening, and current-album cache handling.

## [0.1.0-dev.9] - 2026-07-21

- Merge each collaborator's changed album components into the canonical Cloud PDF without replacing components owned by other devices.
- Reconcile approved ATD documents by version and hash so a collaborator can enrich the shared album without deleting the existing drawing set.
- Bootstrap complete component manifests for legacy Cloud albums and ignore shadowed pre-`SourceKey` snapshots while preserving distinct same-named source streams.
- Track generated and source-backed album components independently, retain pending local work across Cloud refreshes, and clean temporary merge files after use.
- Carry optional Revit sheet scale metadata into Studio and print it below `Загвар` while leaving scale-free generated pages blank.

## [0.1.0-dev.8] - 2026-07-21

- Add the officially configured DAN organization-import boundary while keeping manual and partially completed organization profiles available.
- Unify organization create, view, edit, save, and cancel behavior around the canonical Cloud ERA organization record.
- Refresh project foundation, organization assignment, membership, source, document, album, and archive slices incrementally so shared Studio mirrors converge without downloading unchanged payloads.
- Clean obsolete Cloud album cache files after revision changes while preserving native RVT and DWG source custody on the member's device.
- Keep the company library in the Studio dark theme while editing, with selection locked and a restrained lighter surface instead of the Windows disabled-control background.

## [0.1.0-dev.7] - 2026-07-18

- Normalize legacy PowerShell 5 update-history wrappers during Studio publication so prior release entries remain intact when a new version is added.

## [0.1.0-dev.6] - 2026-07-18

- Add an explicit edit, save, and cancel workflow for project foundation information, with role-based write access and immutable project-code and Cloud land fields.
- Save Cloud project information through the canonical server API while safely queuing local mirror changes when an older server runtime does not expose the update endpoint yet.
- Preserve pending Studio edits across canonical refreshes and keep the last server snapshot separate from locally authored project information.
- Preserve confirmed design-company assignments during Cloud refresh, avoid repeated company selection, and retain assignment history when the canonical company changes.
- Unify Studio dialogs and native window chrome with the dark product theme and remove the remaining bright separator borders.

## [0.1.0-dev.5] - 2026-07-18

- Restrict company management to explicit active organization owners and administrators; cached or directory-only company records can no longer be claimed or edited as the current user's organization.
- Require a fresh canonical Cloud company selection before assigning a design organization to a project or generating its company snapshot.
- Add Studio notification handling for invitations, membership decisions, project-exit requests, and organization-aware project removal.
- Allow a newly created project to synchronize its Studio-generated album pages before any Revit, AutoCAD, or other native source is linked.
- Keep album rebuilding and Cloud synchronization independent from the PDF preview file lock by using versioned preview copies.
- Improve cover approval-table word wrapping and keep long personal names intact.

## [0.1.0-dev.4] - 2026-07-18

- Keep the maximized custom Studio window inside the active monitor's Windows work area, including secondary displays with their own taskbar and DPI.
- Clarify that a linked RVT sends its vector PDF and manifest from Revit's Album workflow while the native file remains local.
- Add a direct action for opening a linked native source from the Studio source workspace.
- Stamp the generated Demo setup executable with the requested release label and assembly version, with a packaging gate that rejects mismatched metadata.

## [0.1.0-dev.3] - 2026-07-18

- Refresh Cloud ERA project roles and scopes automatically when an older local mirror opens.
- Restore `team.manage` and `concept.write` access so authorized Project Admin users can invite members and sync without recreating the project.
- Render album previews and thumbnails from versioned local cache copies instead of locking the canonical generated PDF.
- Keep album rebuild and Cloud sync available while the album PDF is open in Studio.

## [0.1.0-dev.2] - 2026-07-17

- Kept team invitations in Pending state until the recipient explicitly accepts or declines.
- Clarified the Studio team action as `Багаас хасах` for active members and `Урилга цуцлах` for pending invitations.
- Added separate confirmation text so removing a member cannot be confused with revoking an invitation.
- Added mandatory relationship-boundary acknowledgement for project creation, company grants, organization assignment, team invitations, membership acceptance, removal, exit decisions, and source custody transfer.
- Added organization-approved project exit requests and notification handling instead of allowing a member to leave a live project immediately.
- Preserved cloud source metadata when a local native source is relinked, and added explicit cloud source binding and custodian reassignment tools.
- Kept RVT/DWG paths and native files local while synchronizing only source identity, manifest, document, report, and PDF data.
- Added the neutral-platform responsibility model in `docs/RELATIONSHIP-BOUNDARY.md`.

## [0.1.0-dev.1] - 2026-07-17

First complete source snapshot in `munkhkholboo1/Erk-S_Studio`.

- Added the project-centered Studio shell and local/cloud project catalog.
- Added project foundation, company render projection, sources, albums, reports, and archive workspaces.
- Added Revit source discovery, sheet intake, ordering, thumbnail, and PDF album composition foundations.
- Added high-quality Studio-generated concept album pages and project/company-driven page information.
- Added Studio account, profile image, license session, cloud project mirror, and product update foundations.
- Added exact-account project team invitations with multiple roles and explicit Accept/Decline consent.
- Added one-time company-authorized project creation grants without exposing the private company profile.
- Added product packaging for the free `Demo V0.001` distribution.

## Historical Product Milestone

### Demo V0.001 - 2026-07-17

The first free packaged demo was produced before this Git repository had source history. It is recorded
here as a product milestone, not as a historical Git tag pointing to an unverifiable earlier source tree.
