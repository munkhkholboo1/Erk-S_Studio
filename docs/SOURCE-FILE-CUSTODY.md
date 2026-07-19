# Source File Custody Policy

Status: product architecture policy, not a substitute for legal agreements

## Boundary

Erk-S does not upload, transmit, store, back up, recover, escrow, or take custody of RVT, DWG, or
other professional native authoring files. Native files remain under the control of the participant
or organization that stores them.

Studio may store:

- source identity and type;
- local file binding/path on that participant's device;
- producer/document metadata;
- package manifest and SHA-256 values;
- verified vector PDF sheets;
- generated album/report PDFs and revision metadata;
- source custodian assignment and audit history.

A local path is device-specific and is not shared as proof that another participant can access the
file. Cloud APIs must not accept native file content.

## Custodian assignment

A source custodian is the participant authorized to maintain that source's cloud metadata and local
binding. Reassignment changes permission and responsibility metadata only. It does not prove:

- physical/electronic file delivery;
- ownership, copyright, authorship, or reuse rights;
- completion, quality, payment, or contractual acceptance;
- transfer of professional liability or confidentiality obligations.

The receiving participant binds a replacement local file after the parties complete any handover
outside Erk-S. Studio then accepts only newly exported, verified sheet packages.

## Member offboarding

When a member leaves or is removed:

- the platform warns both sides before the relationship-changing action;
- an exit request requires the project creator organization's decision;
- affected source records and audit history remain visible to authorized project managers;
- sources for which the participant was custodian become unassigned until reassigned;
- the participant's local native files remain untouched;
- already released PDF revisions remain immutable;
- no metadata action is described as native-file handover.

The parties remain responsible for contracts, payment, confidentiality, retention, backup, and
file transfer. Platform notifications help make the state visible but do not decide a dispute.

## Exceptional continuity

Loss of account access, illness, death, device loss, organization closure, or another continuity
event does not authorize Erk-S to obtain a native file it never held. An authorized organization
manager may reassign cloud source custody and bind a legally obtained replacement copy. Identity,
succession, employment, and file-access questions must be resolved under applicable law and the
parties' own agreements.

## Prohibited behavior

- Do not add native-file upload fields to a sheet package or Cloud API.
- Do not copy a native file into project delivery, album, release, or source-control folders.
- Do not expose another participant's local path or organization-private source details broadly.
- Do not claim a hash/manifest proves authorship, ownership, or contractual handover.
- Do not delete released records to hide prior participation or custody history.

See `RELATIONSHIP-BOUNDARY.md` for the broader neutral-platform policy.
