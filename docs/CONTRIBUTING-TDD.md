# TDD Contribution Guide

Status: required engineering practice

## Rule

Every feature begins with a failing test. Every bug fix begins with a regression test that reproduces
the defect. Production code is changed only after the test demonstrates the missing behavior.

Use the cycle:

1. **Red** - add the smallest meaningful failing test.
2. **Green** - implement enough behavior to pass without weakening a boundary.
3. **Refactor** - improve design while the suite remains green.
4. **Verify** - run focused tests, then the full affected suite and build.

## Test layers

- Unit tests: pure geometry, policy, reconciliation, lifecycle, and mapping.
- Contract tests: schema, OpenAPI, generated client, compatibility, and serialization.
- Security regression: tamper, traversal, links, stale state, unsigned update, wrong publisher.
- Integration tests: package intake, verified-only album build, cloud idempotency/concurrency.
- Golden tests: vector PDF boxes/operators/XObjects/matrices and maintained visual references.
- Host acceptance: actual Revit and AutoCAD exports through the canonical package validator.
- Product smoke: WPF publish, installer payload, isolated install, update verification.
- Performance: scheduled scale cases and regression thresholds.

## Security and PDF changes

Do not change validation, tolerances, path rules, placement matrices, hashing, release gates, or
golden references merely to make a test pass. The test must state the intended contract and include
the failure path. Security-sensitive new code requires at least 90% branch coverage in the enforced
classes; edge-case quality remains more important than a percentage alone.

## Commands

```powershell
dotnet test src\tests\ErkS.Platform.Core.Tests\ErkS.Platform.Core.Tests.csproj -c Release
dotnet build src\ErkS.Studio.slnx -c Release
src\scripts\Test-CloudEraGeneratedClient.ps1
```

For a focused iteration:

```powershell
dotnet test src\tests\ErkS.Platform.Core.Tests\ErkS.Platform.Core.Tests.csproj `
  -c Release --filter FullyQualifiedName~<TestClassOrMethod>
```

Run server tests in the server repository whenever Cloud ERA behavior or OpenAPI changes.

## Pull request checklist

- A failing test was added first.
- The test covers both success and failure behavior.
- Existing backward compatibility was tested.
- Security, privacy, native custody, and relationship boundaries were considered.
- API/OpenAPI/generated client changed together when required.
- PDF changes have reviewed structural/visual golden evidence.
- No credentials, tokens, licenses, native files, project data, or test customer data are included.
- README, architecture, and normative contracts match the implementation.
- Focused and full verification results are recorded.

CI failure is a merge blocker. Repository branch protection must require the Studio and Cloud ERA
checks; this GitHub repository setting cannot be enforced by source code alone.
