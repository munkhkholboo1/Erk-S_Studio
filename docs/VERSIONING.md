# Source Versioning And Backup

## Source of truth

- `main` contains the current integrated source.
- Every source backup is an annotated Git tag on a tested commit.
- **`src/Studio.Version.props` is the only source of the version a build carries.**
  It holds the published version and its assembly version, plus the development
  version used by every ordinary build. Nothing else in the tree decides a
  build's version, so this is the file a release updates.
  There used to be a `VERSION` file at the root as well, on its own numbering. Nothing read it,
  so it drifted: it said `0.1.0-dev.28` while `Studio.Version.props` said `0.001.46`, and the ONE
  integration audit found the pair. It was deleted on 2026-08-23, and
  `StudioReleasePipelineTests.NothingElseInTheTreeClaimsToBeTheVersion` fails if one comes back.
- `CHANGELOG.md` describes product and architecture milestones.
- Build output, installer payloads, local projects, native design files, credentials, and license data are never source history.

## How a build gets its version

`ErkS.Studio.csproj` and `ErkS.Studio.App.csproj` import `Studio.Version.props` and set
`InformationalVersion` from it:

- no `StudioReleaseLabel` given → `StudioDevelopmentVersion` (for example `0.001.47-dev`);
- `StudioReleaseLabel` given → that label verbatim (the release script passes
  `Demo V0.001.47`).

**The `-dev` suffix is load-bearing.** Studio reads its own `InformationalVersion`
to decide whether it is a development build, and a development build does not
enforce the companion licence. A shipped artifact carrying `-dev` would disable
that enforcement with nothing failing and nothing logged, so
`Test-Studio-ReleaseArtifact.ps1` refuses any artifact whose product version
contains it, and `Publish-Studio-Demo.ps1` refuses such a release label up front.

## Version format

- Development snapshot: `v0.1.0-dev.1`
- Demo/pre-release: `demo-v0.001` or `v0.1.0-demo.1`
- Stable release: `v1.0.0`

Do not move or overwrite an existing version tag. Create a new patch or development sequence instead.

## Creating a backup version

1. Update `src/Studio.Version.props` and `CHANGELOG.md`.
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
