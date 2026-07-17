# Source Versioning And Backup

## Source of truth

- `main` contains the current integrated source.
- Every source backup is an annotated Git tag on a tested commit.
- `VERSION` contains the current source snapshot version.
- `CHANGELOG.md` describes product and architecture milestones.
- Build output, installer payloads, local projects, native design files, credentials, and license data are never source history.

## Version format

- Development snapshot: `v0.1.0-dev.1`
- Demo/pre-release: `demo-v0.001` or `v0.1.0-demo.1`
- Stable release: `v1.0.0`

Do not move or overwrite an existing version tag. Create a new patch or development sequence instead.

## Creating a backup version

1. Update `VERSION` and `CHANGELOG.md`.
2. Build and test the exact source to be preserved.
3. Commit the complete intended change.
4. Create an annotated tag: `git tag -a v0.1.0-dev.2 -m "Erk-S Studio v0.1.0-dev.2"`.
5. Push the commit and tag: `git push origin main` and `git push origin v0.1.0-dev.2`.

The `Version source backup` GitHub workflow creates a Release entry for each pushed `v*` or `demo-v*`
tag. GitHub then keeps downloadable ZIP and TAR source archives tied to that immutable commit.

## Restore

- Inspect versions: `git tag --list --sort=-version:refname`.
- Inspect one version without changing files: `git show v0.1.0-dev.1`.
- Restore into a separate branch: `git switch -c restore/v0.1.0-dev.1 v0.1.0-dev.1`.

Never restore over an active working tree with destructive reset commands.
