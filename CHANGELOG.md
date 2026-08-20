# Version History

This file records product milestones from the first Git source baseline onward.
Older implementation work predates this repository and is not represented as fabricated commits.

## [Unreleased]

## [0.001.37] - 2026-08-20

- Report why a post-sync album verification failed instead of the generic "Canonical album PDF could not be downloaded and verified after sync": the refresh caught the real exception, wrote it to the status line, and returned an empty result, so the message named the outcome and never the cause.

## [0.1.0-dev.28] - 2026-08-17

- Keep Studio in the background while navigating album PDF preview pages by removing foreground-window activation, UI Automation focus, and synthetic keyboard input from the preview workflow.

## [0.1.0-dev.27] - 2026-08-17

- Preserve every collaborator-owned Cloud album component during Source Refresh by patching only the current account/device's verified local contribution, and defer local-only replacement until Cloud Sync when no usable canonical manifest is available.
- Rebuild the local working album only after an AutoCAD package has been fully reconciled, so newly received A2 and other format pages appear without requiring a manual Cloud Sync.
- Map cloud-local working previews through their actual merged component manifest instead of the previous server manifest, preventing received source pages from remaining in a false "waiting for source" state.
- Add an accessible Show/Hide control to the Studio sign-in password field while preserving the entered value, focus, validation, and secure clearing behavior.

## [0.1.0-dev.26] - 2026-08-16

- Align the partial general-plan album with BD 30-103-21 section 8.10: 20 required drawings, two optional risk/operations drawings, deterministic ET/IDB marks, robust CityGen metadata matching, and lossless v1-to-v2 migration.
- Show required and optional album completion separately so an intentionally omitted optional drawing no longer makes the project appear incomplete.
- Add an album-wide 1–4 by 1–4 joined-A3 format selector for partial general plans, keep the 12 mm module overlap, use frame-only cover pages, and retain the horizontal working-drawing title block on drawing-list/notes, location-scheme, surroundings-overview, and future generated sheets.
- Use the selected design organization logo consistently across Studio album covers and title blocks; when it is missing or unreadable, show the bundled Erk-S logo with a "Лого байршуул" prompt without substituting for a client logo.
- Fill the horizontal working-drawing title block's architect row from the project's appointed major architect instead of the design-company director signer.
- Let partial general-plan albums assign Architect, Prepared By, and Checked By per page from project-team members, including Ctrl/Shift multi-page updates across the ET and IDB sections.
- Treat the partial general-plan drawing composition as ordering and table-of-contents metadata, and omit unpopulated composition slots from the Album Pages navigator.
- Continue the Cloud ERA project, document, album, and collaboration workflows.
- Continue Revit, AutoCAD, and CityGen source-package integration.

## [0.1.0-dev.25] - 2026-07-30

- Accept and validate AutoCAD sheet-package schema 5 with a per-page print-color mode while keeping older packages backward-compatible as Original.
- Preserve vector PDF delivery for Original, Black & White, and Gray output and expose the selected print mode to package acceptance diagnostics.

## [0.1.0-dev.24] - 2026-07-30

- Make the Cloud ERA server the sole authority for canonical album component and physical page order, and consume its manifest unchanged on every Studio device.
- Publish stable owner/source, section, sequence, sort, and page identities so reopen, Source Refresh, and Cloud Sync converge across admins, collaborators, and devices.
- Preserve each device's verified local source payload in its server-assigned slot while receiving every other contributor's source pages from the canonical Cloud album.
- Canonicalize full and component uploads on the server, request metadata-only reflow when local content is unchanged, and verify upload acknowledgement separately from the canonical download hash.
- Remove explicitly retired source components through server-authoritative CAS maintenance, including sliced sources beyond the client upload limit, without touching another owner's same-key source.
- Reject stale or descriptorless replacements that could erase page identity, while retaining safe legacy snapshots and one-time source-purpose metadata backfill.

## [0.1.0-dev.23] - 2026-07-30

- Keep the latest verified canonical album visible while a collaborator-owned building sub-cover remains pending, instead of clearing or hiding the 41-page preview.
- Defer an unrenderable remote-only sub-cover to its source-owning Studio without aborting unrelated Cloud uploads, deleting the existing component, or acknowledging unfinished work.
- Reconcile canonical rebuild state against the verified component manifest so only genuinely missing building sub-covers repeat in the next sync.
- Prevent company-cache path and locality normalization from repeatedly dirtying unchanged cover, certificate, license, and title-block components while preserving real company, document, and logo changes.
- Explain deferred rebuild state in the UI and operation log while retaining local sources, collaborator sources, organization documents, and deterministic album order.

## [0.1.0-dev.22] - 2026-07-30

- Preserve each source page's building identity and deterministic album order across restart, partial local hydration, source refresh, and Cloud synchronization.
- Carry AutoCAD building-group identity through Studio package reconciliation so collaborator-owned School and other building sheets remain assigned without requiring the native DWG on every device.
- Require and merge exactly the active building sub-covers for each canonical source slice, while ignoring empty groups and stale inactive assignments.
- Map canonical Cloud PDF preview pages through the shared manifest so Studio opens the exact generated, source, visualization, and title-block page after canonical merges.
- Keep canonical component acknowledgement pending until the requested source, removal, and required sub-cover changes are verifiably present.

## [0.1.0-dev.21] - 2026-07-30

- Recover verified local sources created before device-binding metadata was introduced, using a versioned one-time upgrade that never adopts an ambiguous, foreign, or unverified source.
- Keep the exact immutable owner and device's local source authoritative in the working album after Cloud acknowledgement, while other members continue to receive that stream through Cloud.
- Remove a local source and only its album pages immediately, retain collaborator pages, and queue an idempotent Cloud retirement without modifying the native source file.
- Preserve owner-ambiguous legacy album components instead of retiring another contributor's same-key stream, and release the Source Refresh busy state after preparation or scan failures.

## [0.1.0-dev.20] - 2026-07-30

- Crop or mask a PDF source non-destructively, preview and place the result on the real Studio sheet at its physical 1:1 size, and commit a title-block-only scale edit to that Studio format without resizing the drawing.
- Treat only the current verified package or native payload owned by the current account on the current device as local; historical packages, account switches, absent payloads, foreign accounts, and other devices remain Cloud until an explicit relink.
- Refresh only this device's local source contributions while retaining the current canonical preview when collaborator pages are Cloud-only, then receive the complete canonical union through Cloud Sync.
- Merge participant-owned source components into one deterministic canonical album while preserving stable source and building order through refresh, add, remove, custody transfer, restart, and synchronization; overlapping building edits now use a fail-closed three-way conflict check.
- Protect canonical project, organization, source-stream, and album changes with explicit concurrency tokens and base revisions, and expose a durable rebuild/tombstone signal whenever the canonical PDF lags the accepted building composition.
- Record correlated Studio and server operation diagnostics with safe reason codes, trace IDs, progress, conflicts, and redacted context; production deployment also requires a clean origin commit with successful exact-commit CI.

## [0.1.0-dev.19] - 2026-07-29

- Edit a selected PDF source page directly from Sources with non-destructive crop, rectangular or polygon masks, rotation, and Studio-sheet placement preview.
- Preserve the cropped PDF at its exact 1:1 physical size and keep the editor preview aligned with the generated vector PDF, including centered MediaBox and CropBox origins.
- Store the title-block drawing scale as independent metadata so values such as `1:100` never resize the source drawing.
- Keep PDF source selection readable in the dark theme and distinguish legacy album snapshots without discarding current source contributions.

## [0.1.0-dev.18] - 2026-07-28

- Merge collaborator-owned album components by canonical source identity without replacing another device's source contribution or duplicating Studio-generated pages.
- Keep building sub-covers, source-authored sheets, disabled PDF pages, and restored PDF pages in one deterministic album order across rebuilds and Cloud synchronization.
- Preview each Cloud synchronization before transfer, showing the authoritative project, organization, map, source, and album changes that will be sent or received.
- Render PDF source thumbnails from their real pages, preserve bookmark titles, and restore re-enabled pages to their original source position and settings.
- Keep project chat available beside the album while retaining role-based ownership for project, organization, map, and source updates.

## [0.1.0-dev.17] - 2026-07-27

- Upload Cloud ERA album components as bounded individual requests instead of one oversized multipart payload.
- Chain the returned album revision and project concurrency token across component uploads so collaborator changes remain ordered.
- Report the exact component rejected by a server size limit while allowing the server to accept one bounded large PDF component safely.

## [0.1.0-dev.16] - 2026-07-25

- Keep generated concept-album pages in one immutable semantic order even when legacy local or Cloud metadata contains drifted numeric order values.
- Place the approved architectural planning task directly after the design-license pages and before the location scheme, surrounding context, and general-plan sheets.
- Recognize collaborator-owned `foundation-atd` components as the canonical planning-task section instead of treating them as unassigned sources at the end of the album.
- Invalidate previously normalized album caches so existing projects rebuild once with the corrected deterministic page order.

## [0.1.0-dev.15] - 2026-07-24

- Keep every building sub-cover immediately before that building's sheets while preserving the page order authored by each Revit or AutoCAD source.
- Use one canonical project-name projection for generated covers and every title block, and migrate existing albums to the corrected renderer.
- Classify general-plan and building sources explicitly so multi-source building sets remain deterministic across local refresh and Cloud ERA synchronization.
- Preserve project chat and site-context editing access from canonical project/source metadata instead of device-local state.

## [0.1.0-dev.14] - 2026-07-24

- Combine Revit and AutoCAD sheets from multiple sources into ordered building groups without uploading native RVT or DWG files.
- Keep shared building composition and component ownership canonical across Cloud ERA collaborators.
- Restrict location-scheme editing to the current general-plan source custodian while keeping the result visible to every project member.
- Preserve project-scoped map, source, generated-document, and album state during refresh and Cloud synchronization.

## [0.1.0-dev.13] - 2026-07-24

- Keep project chat available beside every project workspace, including Album and inline map editing.
- Preserve direct chat history for accepted project members without depending on online presence.
- Match the website's Fluent animated emoji in quick send, pickers, messages, and reactions.
- Clip registration and license source pages inside their generated document tiles.
- Show DevMod as the next Demo version after the currently published Studio release.

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
