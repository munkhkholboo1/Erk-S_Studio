# Where a device fingerprint is written (Studio), and what a change to it breaks

Prepared: 2026-08-24, after the move to the canonical `Erk-S device v1` salt
stopped Studio taking in every delivery. For SRV's
`TASK-DEVICE-FINGERPRINT-SPEC.md`, §3 (migration).

## What happened

The spec's migration covered the fingerprint as an **identity sent to the
server**: activation, session, refresh, and the licence cache all learned to
send and recognise both forms. That is one of its roles. The other is as a
**value written into local records to say "this machine holds this"**, and no
migration reached those. Every one of them compared for exact equality, so
after the salt changed they all stopped matching a machine that had not
changed at all.

Studio refuses a package from a source not bound to this account and device.
Nothing quarantines such a package - it is not a bad package - so nothing was
recorded and nothing was shown. The user exported three times into a project
that looked untouched.

## Every site, in Studio

| # | Where | Stored as | Compared in | Covered by the original migration |
|---|---|---|---|---|
| 1 | Server device records | server-side | `ValidateStudioCloudLicense` | yes |
| 2 | Licence/companion cache | Windows Credential Manager blob | `StudioCompanionPolicy.ReadStoredGrant` | yes (dual recognition + rewrite) |
| 3 | **Design source binding** | `local.bindingDeviceFingerprint` in the project file, once per source | `StudioLocalSourceBindingPolicy.IsLocal` | **no - this is what broke** |
| 4 | **Controlled document binding** | `LocalBindingDeviceFingerprint` on each document | `StudioAuxiliarySourceLocalityPolicy.BindingMatches` | **no** |
| 5 | **Visualization image binding** | `LocalBindingDeviceFingerprint` on each image | same | **no** |

Sites 3-5 are all in project files, so they travel with the project and there
is one copy per source, per document and per image - not one per machine.

## What this suggests for other products

Any product that writes the fingerprint into its own records, rather than only
sending it, has the same exposure. Worth checking wherever a product records
"this machine holds this file": local source bindings, cached entitlements,
per-asset ownership marks, and anything that decides whether local content may
be edited.

The failure is quiet by construction: an identity check that stops matching
does not produce an error, it produces an absence. That is what makes it worth
enumerating the write sites rather than waiting for a report.

## What Studio did

Both forms are accepted for the same machine through one rule
(`StudioLocalSourceBindingPolicy.MatchesBoundDevice`), and a record naming the
device by the older form is rewritten to the canonical one the next time it is
used. Nobody relinks anything. Separately, a refused package now states its
reason, so the next identity change of any kind cannot be silent.

## Suggested addition to the spec

§3 currently describes dual recognition for the server exchange. It is worth
saying explicitly that a client must enumerate **every place it writes the
fingerprint**, not only where it sends it, and migrate each - and that a
comparison used to decide access should never be an exact match against a
single stored value while a migration is in progress.
