# Project stage lifecycle

One Cloud ERA project retains the complete design history. A project stage is
never implemented by overwriting the project's global design organization.

## Boundaries

- `CreatedByOrganization` is immutable provenance, not permanent access.
- Each stage has its own organization-assignment history.
- A downstream organization may read its predecessor stages.
- An outgoing organization cannot read downstream stages unless separately assigned.
- Native RVT, DWG, and other authoring files never cross the platform boundary.
- Album revisions retain the organization snapshot effective when the revision was created.
- Replacing an organization ends the earlier assignment and creates another assignment;
  it never rewrites earlier revisions, title blocks, or audit records.

## Stage transition

1. Select an approved or released album revision as the immutable handover basis.
2. Complete the current stage and create the successor with a predecessor link.
3. Invite the successor organization.
4. The successor accepts the assignment and receives the new stage scope.
5. The outgoing organization loses any global project-admin bypass unless it also
   holds an independent client role.
6. The successor receives predecessor PDFs, metadata, decisions, and other scoped
   basis records, but no native authoring file or unrelated private draft.

## Code organization

Project-type stage definitions are isolated under `ProjectTypes/`. Do not combine
building design, urban planning, engineering networks, road/bridge, landscape, or
redevelopment definitions into one switch or one monolithic source file. The shared
lifecycle engine may depend on `IStudioProjectTypeDefinition`; type-specific stage
and document rules belong to the corresponding provider file.
