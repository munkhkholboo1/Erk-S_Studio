# Version History

This file records product milestones from the first Git source baseline onward.
Older implementation work predates this repository and is not represented as fabricated commits.

## [Unreleased]

- Let the portfolio build its PDF at all. The writer asked the finished document how many pages it had written, but PdfSharp seals a document when it is saved and refuses every question about it afterwards - so building a portfolio threw immediately after writing the file, and Studio reported «Портфолио үүсгэсэнгүй» over a PDF sitting complete on disk, never recording the build or offering to open it. The count is now taken before the document is sealed. Broken since 0.001.44.
- Let a page taken out of the portfolio stay out, and let it come back. Removing an imported page deleted it, so the next export from the same drawing put it straight back and the user had no way to say otherwise; removing it now hides it instead. Hidden pages keep their place, their wording and their drawing, are shown by «Хассаныг харуулах» and returned by «Сэргээх», and are left out of the built presentation meanwhile.
- Keep the name the user gave a portfolio page. A re-import overwrote the title with the drawing's own every time, so a page renamed for the presentation lost that name at the next export. A title the user typed is now theirs and is left alone, while one they never touched keeps following the source - and the page can be renamed at all, which the inspector previously offered no way to do.
- Say when a portfolio page is no longer in the drawing it came from. A full snapshot that omits a page does not remove it - a portfolio is the project's own presentation - but nothing said why the page was still there. It now reads «Эх багцад алга» in the list, so its presence is explained rather than puzzling. A delta package, which says nothing about what it leaves out, never marks a page this way.
- Clear away portfolio files nothing shows any more. Portfolio files are content addressed, so re-exporting a changed page wrote a new file and left the previous one in the project for good. Files no page refers to are now removed after an import, and a page the user hid still counts as referring to its own, so it can be restored with its drawing intact. A portfolio file was also recorded in a project document list that nothing ever read; the page itself is now the single account of what the portfolio holds.
- Stop cropping an imported portfolio page. A page authored for the portfolio arrives with its own 10 mm margin, so it was placed full-bleed rather than contained, which would have framed that margin a second time. But full-bleed fills the page and crops whatever falls outside it, so any page whose shape differed from the portfolio's own lost drawing off its edges - the one thing the intake contract says a portfolio must never do. Such a page is now fitted to the page edge: no margin is added and nothing is cut away, and the choice is offered in the layout list as «Захгүй, бүтнээр» beside the two that were already there.
- Say when a package brings portfolio pages. They arrive in the same package as album sheets but are filed somewhere else entirely, so nothing reported them: a user watching the portfolio saw the list sit still, and a user anywhere else was never told the pages had come at all. The portfolio list now refreshes as the package lands, and the status line says how many pages were added and how many were updated.

## [0.001.47] - 2026-08-23

- Keep an existing Studio activation while device identity moves to the shared Erk-S fingerprint. Activation, validation, sign-in, and refresh now send the canonical fingerprint together with Studio's legacy alias, so the server can recognize one machine instead of consuming another device slot. A legacy device-bound companion grant remains valid in Windows Credential Manager and is rewritten to the canonical fingerprint after the first successful validation, without asking the user to sign in again.
- Take portfolio pages from a sheet package into the project portfolio. PFA now marks a page `destination: Portfolio` - a drawing the project wants to present, not to bind - and Studio filed every incoming sheet into the album regardless. A portfolio entry now never enters the sheet library or any album composition; it becomes one portfolio item whose PDF is copied into the project's own storage, keyed by source identity and sheet id so a re-export updates the same item - content, title and page reference follow the source, while the ordering, caption, layout and focal point the user set stay theirs. The page arrives with its own 10 mm margin and is placed full-bleed, since fitting it inside a margin would frame it twice.
- Refuse to ship a build that would not enforce its own licence. Studio reads its version string to tell a development build from a released one, and a development build does not enforce the companion licence - so a release label carrying `-dev` would have shipped a product that quietly stopped checking, with nothing failing and nothing logged. The release artifact gate now rejects any artifact whose product version carries that marker, and the publish script refuses such a label before the build starts. The versioning and release documents pointed at a `VERSION` file that no build has ever read; they now name `Studio.Version.props` and `StudioReleaseLabel`, which are what actually decide a build's version, and the unread file is gone.
- Leave the licence check out of unattended acceptance runs. The CI product smoke test and the release script's install and update checks publish with a release label and then launch the result, so the new licence enforcement would have applied to them - putting a prompt in front of a run with nobody to answer it.
- Open Studio only for an account that holds an active licence. Studio stays free, but it is a companion to the Platform and CityGen programs rather than a product of its own, so it now asks the server what the account is entitled to and opens on that answer. The rule is deliberately one-sided: a server that says nothing about entitlements is an older deployment, not an account without a licence, and Studio opens; only an explicit refusal closes it. A confirmed grant is kept in the credential store, bound to the device that earned it, and carries Studio through seven days without a server - or until the granting licence expires, whichever comes first. A grant stamped in the future is read as a clock moved back to stretch that window, and refused. Enforcement is off for development builds and for a loopback server, and there is no setting that turns it off for a released build against the live site.
- Ask the server whether the state-registry (ДАН) import exists before calling it. The import endpoints were the only Cloud ERA calls made without a capability gate, so a server with no ДАН connection answered with a raw 503; the client now requires the server to advertise dan-organization-registry-import-v1 and refuses with a controlled message when it does not.
- Read the recovery token a refused organization save returns. A 412 carries the organization's current canonical token, but the client discarded it and told the user to reload the whole company library first; the token is now kept on the entry, so comparing and deliberately re-saving works without the re-list.
- Keep the session's bearer token on the session's own server. The profile photo and the organization logo were fetched from whatever URL the server's response named, with the token attached - so a response naming another host would have carried the token there. A server-supplied address is now resolved against the session server and refused when it lands anywhere else; a refused logo simply does not display, and a refused profile-photo address falls back to the canonical endpoint.
- Recognize the Portfolio page-format mode as its own chromeless kind. An unknown format mode falls back to working-drawing chrome, so a portfolio-formatted page that ever slipped into an album would have a title block stamped over a page that declares none; such a page now draws no chrome at all, and an imported portfolio page names itself "CAD хуудас" in the portfolio list instead of posing as a project image.
- Treat a package's album and portfolio entries as two independent sets. A full snapshot keeps deleting the album sheets it omits, but a portfolio entry cannot mask an album deletion and an album entry cannot mask a portfolio one - and a snapshot that drops a portfolio page leaves the imported item in place, because the portfolio is the project's own presentation and must not lose content. An entry declaring any destination other than Album or Portfolio fails verification and quarantines the package, the way an unknown print color mode does; packages written before the field existed keep meaning Album.

## [0.001.46] - 2026-08-23

- Take comments on the sheets of the album. A page is opened for review, a pin is placed where the remark belongs, and the thread beside it carries the comment, its replies and its state. Three kinds ordered by how much they demand - Засах шаардлагатай, Тайлбар, Зөвшөөрсөн - and two states, нээлттэй and шийдэгдсэн, with who settled it recorded. Reading and writing needs only membership of the project, so a reviewer who may not change a drawing can still say what they think of it; settling or withdrawing needs authorship or the right to the project's content.
- Anchor a comment to the sheet's own key and to a fraction of the page, never to a page number. An album is rebuilt, merged and re-ordered constantly, and a comment that pointed at "page 7" would be pointing at a different drawing by the afternoon. The anchor survives every rebuild, every re-order and a re-issue of the sheet in another format, and the comments live in their own store so the album's canonical merge rules are untouched by them.
- Draw the page under review from the built album rather than from the source drawing, so a reviewer sees the sheet as the album issues it - with its title block, format and Studio layout - and so the pages the album generates, the cover and the drawing list among them, can be commented on at all.
- Say what a project is when it opens. The page led with a form of empty input boxes and read as a settings dialog; it now opens on the design organization's logo and name, the project's own name and stage, the cover its album has reached, and what the project is made of. The record itself is read rather than typed - a field nobody has filled shows as a dash instead of an empty box - and the form appears only once Засварлах is pressed.
- Show the project's team on the page the project opens on, with each member's photograph, their roles in the words the server uses for them, and whether they are active.
- Lay a tab page out from its top-left corner. A TabControl centres its content by default, which floated the project form into the middle of an otherwise empty page.

## [0.001.45] - 2026-08-22

- Open a project from the home page. A card set the project list's selection and then read it back, but the home page is shown while that list holds organization folders - a row that is not in the list cannot become its selection, so the selection stayed empty and opening stopped with "Нээх төслөө сонгоно уу." A project is now named to the open path rather than pointed at through a selection.
- Act on the project whose three-dot menu was opened. Нээх and Устгах / Төслөөс гарах went through the same selection round-trip, so they depended on the list holding that project rather than on the menu that named it.

## [0.001.44] - 2026-08-22

- Open the Studio on a home page of its own. The mark in the rail was decoration; it is now the way back to a page that carries the practice's recent projects, the programs the Platform publishes with the site's own artwork, their current versions and a download for each, and the Partner rights banner. The wording and availability come from the site when it serves them and from this build when it cannot be reached, so a program the site adds needs no new Studio.
- File the project list into folders by design organization rather than one growing wall of cards. A folder shows the organization's logo and how many projects it holds; inside one, the projects are gathered under design-stage headings that name the stage and count it, and can be read as cards or as a compact list. A project is managed from a three-dot menu on the project itself, replacing a single header button that could only ever mean one thing at a time.
- Show a partner organization's own logo. Only organizations this account belongs to had one; the logo of a practice whose project is merely visible here is now fetched through that project, which is exactly the right to see it.
- Give an organization with no uploaded logo a mark made from its name, so it reads as itself rather than as a blank - on folders, on company cards, and in the organization picker.
- Open the company library as logo-and-name cards. Entering the page showed the full record of whichever company happened to be selected; the details now appear when Засварлах is pressed.
- Add a portfolio to a project, beside its album: a freer presentation assembled from the project's own drawings and images, with its own files kept apart from the foundation documents so a presentation asset never enters an approval.
- Draw a contributed album page on the album's own page format. Without it the cover and drawing list of a general plan were composed A4 with a concept corner table - and those were the pages uploaded and kept as the shared album, which is why the stray A4 page returned after every sync.
- Let the device that owns a page replace one an older build drew. Generated pages - cover, drawing list, location scheme - were skipped on every device because they have no source file, so a page composed by an older renderer could never leave the canonical album. Authority over the album's metadata now decides instead, and Бүрэн дахин байгуулах marks those pages for re-rendering so the rebuild reaches the shared album rather than only the local copy.
- Say which part of a canonical album acknowledgement is missing when a sync verification fails. "Incomplete" was the same message whether the album was never resolved or the sync simply had no work to do.
- Show the visualization row on a cloud project before it holds any image. The row is the only place the first image can be added, so hiding it until images existed meant the feature could never be started on a cloud project at all; the edit button now adds images rather than refusing.

## [0.001.43] - 2026-08-21

- Stop serving a preview that was restamped by an older title-block revision. The album pointer a project stores is opened verbatim on load - nothing revalidates it - so 0.001.42's fix went on showing the doubled title block until some other album operation happened to regenerate the file. A pointer whose signature no longer matches is now dropped and the album rebuilt.
- Keep a general-plan album in the order its sheets arrive from AutoCAD. Pages were ordered by template slot, and the slot matcher's numbered branch never fires for an AutoCAD package - its numbers are bare "00".."14" while the slots are numbered "ЕТ-03".. - so every sheet the matcher did not recognise was swept to the end of the album and a sheet in no section landed in a trailing bucket. Sections are now read off the page order as runs rather than imposed on it.
- Drop the legacy A4-portrait table of contents from general-plan albums. The composition already carries the drawing list as a page of its own, so an album created before that emitted a second one in the middle of the set.
- Also refuse the concept corner table on any album whose generated pages are drawn with the working-drawing chrome, whatever its template id says.

## [0.001.42] - 2026-08-21

- Stop painting a second title block onto general-plan and working-drawing sheets. The canonical restamp always repainted the concept album's corner table, at concept coordinates, over pages that already carry the horizontal title block their own build drew - leaving two title blocks, offset from one another, on every sheet of a ХЕТ album. Those albums now keep the one their build drew.

## [0.001.41] - 2026-08-21

- Receive every working-drawing set PFA issues. ДМ (Дулаан механик) and АУ (Автомат удирдлага) had no set at all, and the electrical set carried its mark in the opposite order to the one PFA sends, so its sheets matched nothing; both orders are now recognised on import.
- Make ЕХ (Ерөнхий хэсэг) a set of its own and open every album with it. The cover, the drawing list and the explanatory note were folded into БА, which is not the mark they carry - and they were re-emitted into all six discipline PDFs besides.
- Issue working drawings one album per building rather than one per discipline, the way they are bound. Inside an album the sheets run set by set - ЕХ, БА, ББ, ХАС, ДМ, ЦБУ, ХТ,ДГ, ХД, АУ - while the album's own order still decides within a set. The concept stage composes the opposite way and is unchanged: every building of the project in a single album.
- Add Судалгаа and Бичиг баримт to partial and development general-plan projects, beside the album and the report. Both register files the way the foundation documents do, keeping an owned copy inside the project.

## [0.001.40] - 2026-08-20

- Restrict automatic building-group creation to the building-architecture concept album. It is the only album that builds a section per building and therefore the only one whose writer draws a building sub-cover, so a group created for a working-drawing album demanded a cover that is never rendered and would have failed the cloud sync the same way the urban-planning album did in 0.001.38.
- Let a source whose sheets are already filed under one building join that building instead of creating a second, empty one beside it.

## [0.001.39] - 2026-08-20

- Give an AutoCAD source that turns out to be a building a building group. It is recognised as a building only once its package is read, by which time nobody had picked a group for it, so every sheet it delivered stayed outside the building composition; the group is now named after the drawing.
- Create the building group a package names when the project has never listed it, keeping the id the exporter declared so the next package resolves to the same group instead of adding a second one beside it. Previously such a sheet was skipped and belonged to no building.
- Apply the package's own per-sheet building identity before the source-level default, so a package that files its sheets across several buildings is no longer flattened into one.
- Confine both to albums that compose buildings. A partial or development general plan has no building types at all - a group created there would demand a building sub-cover that album never draws, which blocked the project sync - and the Add-Source dialog no longer offers the building type for those stages. Engineering infrastructure arrives there as its own source.

## [0.001.38] - 2026-08-20

- Classify an AutoCAD general-plan source as Ерөнхий төлөвлөгөө instead of a building. AutoCAD sends the drawing mark as the sheet discipline and the general-plan album marks its general-plan sheets ЕТ, which matches none of the phrases detection looked for, and content kinds arrive as hyphenated template slot ids such as general-plan-zoning, so every general-plan DWG became a building - then required a building sub-cover the album never draws and blocked the project sync.
- Stop assuming a newly added AutoCAD source is a building and pre-filling "Барилга 1" behind the type selector; AutoCAD carries both kinds of drawing, so the package content or the person adding it decides.

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
