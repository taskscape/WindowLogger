# WindowLogger .NET 10 Upgrade Plan

## Table of Contents
- [Executive Summary](#executive-summary)
- [Migration Strategy](#migration-strategy)
- [Detailed Dependency Analysis](#detailed-dependency-analysis)
- [Project-by-Project Plans](#project-by-project-plans)
- [Package Update Reference](#package-update-reference)
- [Breaking Changes Catalog](#breaking-changes-catalog)
- [Testing & Validation Strategy](#testing--validation-strategy)
- [Risk Management](#risk-management)
- [Complexity & Effort Assessment](#complexity--effort-assessment)
- [Source Control Strategy](#source-control-strategy)
- [Success Criteria](#success-criteria)

## Executive Summary
- **Scenario**: Upgrade the solution to `.NET 10.0` with Windows desktop support. Only `WindowLoggerConfigGui` is currently on `net48`; all other projects already target `net10.0`/`net10.0-windows`.
- **Scope**: 4 projects total; single upgrade target is `WindowLoggerConfigGui` (WinForms). One NuGet package with recommended update (`System.Text.Json` 10.0.2 → 10.0.3) in that project.
- **Strategy**: **All-At-Once** (atomic) upgrade—apply framework and package changes together, then restore/build/verify for the whole solution.
- **Complexity**: Topology simple (depth 1), but medium risk for `WindowLoggerConfigGui` due to WinForms binary incompatibilities moving from .NET Framework to modern .NET.
- **Key risks**: WinForms API deltas from .NET Framework, designer-generated code adjustments, resource handling, and JSON serialization package update alignment.
- **Success definition**: All projects compile on `.NET 10.0`/`.NET 10.0-windows`, package updates applied, solution builds cleanly, and UI smoke tests for the tray/config GUI pass.

## Migration Strategy
**Approach: All-At-Once (atomic)**
- Rationale: Small solution (4 projects), shallow dependency graph (tray depends on the other three), only one project requires TF/pacakge change. Coordinated update minimizes multi-targeting overhead.
- Scope of atomic pass: Update `WindowLoggerConfigGui` to `net10.0-windows`, align Windows Desktop settings, update `System.Text.Json`, restore/build entire solution, fix compilation issues, re-build to verify.

**Ordering principles (even in atomic pass)**
1. Apply framework/property/package updates across all affected MSBuild surfaces (project file and any imported props/targets if present).
2. Build solution once to surface incompatibilities; fix all compilation errors in the same pass.
3. Rebuild the entire solution to confirm a clean build; then proceed to UI smoke tests.

**Parallelization**
- Code changes happen in one branch/commit, but build/validation is performed for the whole solution as a single unit.

**Windows Desktop specifics**
- Target `net10.0-windows` in WinForms projects and ensure `<UseWindowsDesktop>true</UseWindowsDesktop>` (or WindowsDesktop SDK already implied by project type).

## Detailed Dependency Analysis
- **Graph summary**: `WindowLoggerTray` depends on `WindowLoggerConfigGui`, `WindowAnalyser`, and `WindowLogger`. The other three have no project dependencies.
- **Critical path**: Upgrade `WindowLoggerConfigGui` (dependency) so `WindowLoggerTray` can remain on `net10.0-windows` without referencing `net48` binaries.
- **Phased grouping (informational, still atomic execution)**:
  - Group A (leaf libraries/apps): `WindowAnalyser`, `WindowLogger` (already on net10.0)
  - Group B (WinForms dependency): `WindowLoggerConfigGui` (net48 → net10.0-windows)
  - Group C (root app): `WindowLoggerTray` (net10.0-windows, depends on A/B)
- **Cycle detection**: None.

## Project-by-Project Plans

### WindowLoggerConfigGui (WinForms)
- **Current → Target**: `net48` → `net10.0-windows`
- **Packages**: Update `System.Text.Json` 10.0.2 → 10.0.3
- **Framework/SDK settings**: Ensure `<TargetFramework>net10.0-windows</TargetFramework>` and `<UseWindowsDesktop>true</UseWindowsDesktop>` (or WindowsDesktop SDK).
- **Migration steps**:
  1. Update TF to `net10.0-windows` in project file (and any Directory.Build.props if present).
  2. Ensure Windows Desktop workload import remains (WinForms templates usually implicit with SDK-style when targeting `-windows`).
  3. Update `PackageReference` for `System.Text.Json` to 10.0.3; `Newtonsoft.Json` 13.0.4 remains as-is per assessment compatibility.
  4. Re-run restore and full solution build to surface WinForms binary incompatibilities (designer-generated code, event handlers, Control collections, TableLayoutPanel usage, etc.).
  5. Fix compilation issues in designer/code-behind (common fixes: namespace resolution, event handler signatures, ControlCollection/table layout APIs, docking/padding enums, resource access patterns). No API removals expected, but net48→net10 brings type rebind.
  6. Verify resource files (`.resx`) compile; ensure any strongly-typed resources regenerate if needed.
  7. Rebuild entire solution and run UI smoke for config interactions and serialization scenarios that use `System.Text.Json`.
- **Expected breaking areas**: WinForms binary recompile (Controls, DockStyle, TableLayoutPanel, Button/TextBox members), potential `System.Drawing` usage; adjust using statements if compiler surfaces namespace changes. Serialization: no breaking change expected for 10.0.3 bump but rebuild validates.
- **Validation**: Solution build succeeds; config UI loads in smoke test; tray interaction with config assembly works.

### WindowLogger (library)
- **Current → Target**: `net10.0` (no change)
- **Packages**: None flagged.
- **Plan**: Keep targeting `net10.0`; rebuild after config GUI upgrade to ensure downstream compatibility.
- **Validation**: Clean build; confirm public API consumed by tray remains compatible.

### WindowAnalyser (library)
- **Current → Target**: `net10.0` (no change)
- **Packages**: `ClosedXML` 0.105.0-rc (compatible), `Newtonsoft.Json` 13.0.3 (compatible).
- **Plan**: Retain TF and packages; rebuild after solution-wide changes.
- **Validation**: Clean build; ensure tray consumption remains intact.

### WindowLoggerTray (WinForms)
- **Current → Target**: `net10.0-windows` (no change)
- **Packages**: `System.Drawing.Common` 10.0.3 (compatible).
- **Plan**: No TF change; rebuild after `WindowLoggerConfigGui` upgrade to confirm dependency re-targeting is valid.
- **Validation**: Full solution build; UI smoke to confirm tray loads config GUI dependency without load errors.

## Package Update Reference

| Scope | Package | Current | Target | Projects | Reason |
| :--- | :--- | :---: | :---: | :--- | :--- |
| Common WinForms dependency | System.Text.Json | 10.0.2 | 10.0.3 | WindowLoggerConfigGui | Recommended update from assessment; keep patch alignment with .NET 10 SDK binaries |

_No other package updates required per assessment; Newtonsoft.Json (13.0.4 / 13.0.3), ClosedXML (0.105.0-rc), and System.Drawing.Common (10.0.3) remain compatible._

## Breaking Changes Catalog
- **WinForms binary recompile (net48 → net10.0-windows)**: Control collections, layout enums (DockStyle, SizeType), TableLayoutPanel collections, event handler signatures must rebind against new assemblies. Expect compile-time errors; fix in designer/code-behind as surfaced.
- **System.Drawing on Windows**: Project already Windows-only; keep usage within Windows Desktop scope. No server-side scenarios expected.
- **Serialization**: `System.Text.Json` 10.0.2 → 10.0.3 is a patch update; minimal risk. Validate any custom converters/options still compile and behave.
- **Windows Desktop SDK**: Ensure WindowsDesktop workload is available; `<UseWindowsDesktop>true</UseWindowsDesktop>` or WindowsDesktop SDK stays present when targeting `net10.0-windows`.

## Testing & Validation Strategy
- **Build validation (atomic)**: `dotnet restore` then build entire solution after TF/package updates; rebuild after fixes to confirm 0 errors/warnings.
- **WinForms UI smoke (post-upgrade)**:
  - Launch `WindowLoggerConfigGui` to verify forms load, controls render, event handlers wired.
  - Launch `WindowLoggerTray` to confirm it loads updated config assembly without type load errors and tray interactions still work.
- **Serialization check**: Exercise code paths using `System.Text.Json` to ensure options/converters still serialize/deserialize as expected.
- **Regression guard**: Spot-check core flows: logging configuration, tray interactions, any file I/O paths managed by the config GUI.
- **Artifacts**: Expect clean build output and functioning desktop apps; no automated tests listed in assessment—manual smoke required.

## Risk Management

| Project | Risk | Reason | Mitigation |
| :--- | :---: | :--- | :--- |
| WindowLoggerConfigGui | Medium | WinForms API binary incompatibility from net48 to net10.0-windows; designer code may need fixes; System.Text.Json patch update | Atomic build/fix loop; verify `<UseWindowsDesktop>`; rebuild designer if needed; run UI smoke and serialization check |
| WindowLoggerTray | Low | Depends on upgraded config GUI | Rebuild after dependency upgrade; UI smoke to confirm load |
| WindowLogger | Low | Already on net10.0 | Rebuild to confirm no regressions |
| WindowAnalyser | Low | Already on net10.0 | Rebuild to confirm no regressions |

**Contingencies**
- If WindowsDesktop workload missing, install it before build.
- If designer regeneration fails, adjust partial classes manually where compiler points (ControlCollection/TableLayoutPanel usage).
- If System.Text.Json behavior regresses, temporarily pin to 10.0.2 and re-evaluate, but target remains 10.0.3 per plan.

## Complexity & Effort Assessment

| Project | Complexity | Drivers |
| :--- | :---: | :--- |
| WindowLoggerConfigGui | Medium | WinForms re-target from net48, designer fixes, single package update |
| WindowLoggerTray | Low | Rebuild against upgraded dependency |
| WindowLogger | Low | No TF/package changes |
| WindowAnalyser | Low | No TF/package changes |

Overall solution complexity: simple topology, single upgrade target; effort concentrated in WinForms recompilation and minor package bump.

## Source Control Strategy
- **Branching**: Work on `upgrade-to-NET10` (already checked out from `main`).
- **Commit model**: Single atomic commit for the All-At-Once upgrade (framework + package + fixes) to preserve coherency.
- **Review**: Open PR back to `main`; include build output summary and UI smoke notes in the PR description.
- **Change isolation**: No partial commits; avoid mixing non-upgrade changes.

## Success Criteria
- All projects target `.NET 10.0`/`.NET 10.0-windows` as applicable; `WindowLoggerConfigGui` updated from `net48`.
- `System.Text.Json` updated to 10.0.3 in `WindowLoggerConfigGui`; all other assessed packages remain compatible.
- Solution restores and builds cleanly with 0 errors.
- WinForms binaries load at runtime: config GUI launches, tray launches and interacts with updated config assembly.
- Serialization paths using `System.Text.Json` function as before.
- No new warnings or regressions observed in smoke tests.
