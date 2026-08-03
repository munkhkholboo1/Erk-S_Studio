# Erk-S Studio project guidance

## Project type architecture

- Keep every design-project type in its own source file under `src/src/ErkS.Platform.Core/ProjectTypes/`.
- Do not merge building, urban-planning, engineering-network, road/bridge, landscape, or redevelopment stage definitions into one monolithic catalog or switch.
- Shared stage lifecycle and access behavior must depend on the project-type interface, not concrete type checks.
- A project keeps immutable stage and assignment history; never replace historical organization snapshots with the current organization profile.
- Native RVT, DWG, and other authoring files must never be uploaded or implied to have been transferred by a stage assignment.

## Verification

- Begin behavior changes with a failing test.
- Run the focused test, then `dotnet test src/ErkS.Studio.slnx -c Release`.
- When Cloud ERA contracts change, regenerate and verify the committed generated client.
