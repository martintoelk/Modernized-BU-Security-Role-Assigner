# Modernized BU Role Assigner — XrmToolBox plugin

Handover context for Claude Code CLI. This file is the canonical project brief; read it
before touching code.

## What this is

An XrmToolBox plugin (C# / WinForms, .NET Framework 4.8) that bulk-assigns and removes
Dataverse **security roles** on **teams or users**. A toolbar toggle switches the left list
between the two (never mixed in one operation); the right list is security roles, showing each
role's **business unit**. Actions: *Add roles to team(s)/user(s)* / *Remove roles from
team(s)/user(s)*, with a **Remove from all BUs** opt-in for the remove case.

Built for the **modernized business units** (matrix data-access) model, where a team or user can
hold security roles from any BU — so the default behavior assigns the exact role selected,
keeping its BU. A legacy classic-BU code path exists, auto-detected via a behavioral probe
rather than a manual toggle.

## Current status

Builds clean, split into `BuMatrixSecurityRoleAssigner.Core` (class library, no WinForms/XTB
dependency) and `BuMatrixSecurityRoleAssigner` (the thin XTB plugin project). Published on
NuGet.org / the XrmToolBox Tool Library as `BuMatrixSecurityRoleAssigner`; the publish workflow
passes the target version into the build so the shipped DLL's assembly version always matches
the package version. Smoke-tested against a live dev org — successful.

## File map

| File | Purpose |
|------|---------|
| `BuMatrixSecurityRoleAssigner.Core/TeamRoleAssignmentService.cs` | Team/user/role data access + add/remove logic; depends only on `IOrganizationService` |
| `BuMatrixSecurityRoleAssigner.Core/Models.cs` | `TeamItem`, `UserItem` (both implement `IAssignmentTarget`), `RoleItem`, `OperationLog` |
| `BuMatrixSecurityRoleAssigner.Core/Generated/Entities/*.cs` | Early-bound Dataverse entity classes (`Team`, `Role`, `TeamRoles`, `SystemUser`, `SystemUserRoles`, `BusinessUnit`) — **generated, don't hand-edit**. Regenerate with `pac modelbuilder build --entitynamesfilter "team;role;systemuser;businessunit;teamroles;systemuserroles" --outdirectory "BuMatrixSecurityRoleAssigner.Core/Generated" --namespace "BuMatrixSecurityRoleAssigner.Core.Entities" --emitfieldsclasses` |
| `BuMatrixSecurityRoleAssigner.Core/BuMatrixSecurityRoleAssigner.Core.csproj` | SDK-style class library (net48) |
| `BuMatrixSecurityRoleAssigner.Core.Tests/` | xUnit test project + hand-rolled `FakeOrganizationService` double — no live org needed |
| `BuMatrixSecurityRoleAssigner/Plugin.cs` | MEF export + XTB metadata (plugin factory, tile icons) |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.cs` | UI wiring + threading (`WorkAsync`), delegates logic to Core |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.Designer.cs` | WinForms UI (toolstrip with Teams/Users toggle, three-column layout, status bar) |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssigner.csproj` | SDK-style project (net48, `UseWindowsForms`), references Core |
| `BuMatrixSecurityRoleAssigner.slnx` | Solution referencing all three projects |
| `BuMatrixSecurityRoleAssigner.nuspec` | NuGet package spec |
| `nuget/icon.png` | Package icon (reuses `Plugin.cs`'s `BigImageBase64` tile image) |
| `.github/workflows/publish-nuget.yml` | Manual `workflow_dispatch` — builds, packs, and pushes to NuGet.org via Trusted Publishing (OIDC) |
| `.claude/skills/nuget-checklist-publish/` | Validates the XrmToolBox Tool Library submission checklist against this repo, then publishes — see that skill for nuget/nuspec gotchas found the hard way |
| `README.md` | End-user install/build/deploy/use notes |

## Architecture & key decisions

- **Early-bound entities.** `TeamRoleAssignmentService` uses the generated classes under
  `Generated/Entities` (`Team`, `Role`, `TeamRoles`, ...) instead of magic-string entity/attribute
  names — `Team.EntityLogicalName`, `Role.Fields.BusinessUnitId`, etc.
- **N:N relationship** used for assignment is `teamroles_association` (intersect entity
  `teamroles`), via `IOrganizationService.Associate` / `Disassociate`.
- **`IAssignmentTarget`** unifies `TeamItem` and `UserItem` so the add/remove path doesn't care
  which is selected; the UI's Teams/Users toggle just changes which list is populated.
- **Default = assign exact role.** The role the user selects is associated as-is, whatever its
  BU. Correct for modernized BUs; this is the intended primary path.
- **Classic path (auto-detected).** No manual toggle. The exact-role (modernized) association is
  tried first; if it faults for a target, that's treated as the signature of a classic-BU org and
  the role is resolved to the copy in the target's own BU, then retried once. Resolution key is
  `parentrootroleid`, which is identical across all BU copies of a logical role (`RootRoleId` in
  `RoleItem`; falls back to the role's own id for root-BU roles). A successful retry is surfaced
  via `OperationLog.ClassicBuDetected` (a warning, never a silent behavior switch). Targets with
  no copy of the role in their BU are skipped and reported under `NoRoleInBu`.
- **Remove from all BUs.** Remove-only opt-in (`chkRemoveAllBus`, default off): normal remove
  only touches the exact role/BU pair(s) selected; checked, it widens each selection to every BU
  copy of that logical role (via `RootRoleId`) currently assigned to the selected targets.
- **Idempotent.** Each target's current roles are read first (`GetExistingRoleIds`), so add skips
  already-assigned pairs and remove skips not-assigned pairs — no duplicate-key errors, safe to
  re-run.
- **Access teams** can't hold security roles; the `Associate`/`Disassociate` error is caught
  per target and surfaced in the summary without aborting the batch.
- **Paging** on all loads (`PagingInfo`, 5000/page + paging cookie) so it won't truncate large
  orgs at 5k.
- **Progress in role units.** `AssignOrRemove` denominates progress in (target × role) units, not
  targets, and (dis)associates in batches of `RoleBatchSize` (10) — so the percentage and the ETA
  move *inside* a target instead of only at target boundaries (3 teams × 40 roles would otherwise
  only ever read 0/33/66%). Within a target, reports are throttled to roughly one per batch; every
  target boundary reports regardless, so a run of one role across many targets still moves. Skipped
  units count too, so it always finishes at 100%. Classic-BU warnings and per-target errors are
  aggregated to one line per target, so batching doesn't multiply the summary.
- **Threading** via `PluginControlBase.WorkAsync`; selections are read on the UI thread before
  the background work; progress via `SetWorkingMessage`. Add/Remove call `ExecuteMethod` directly
  (no manual connection check) so the host's own connection dialog opens when disconnected.

## Build

Needs VS 2022 or `dotnet` SDK with the **.NET Framework 4.8** targeting pack + **.NET desktop**
workload (Windows only — WinForms + net48).

```
dotnet restore
dotnet build -c Release
```

Builds both `BuMatrixSecurityRoleAssigner.Core` and `BuMatrixSecurityRoleAssigner` via
`BuMatrixSecurityRoleAssigner.sln`. Output: `BuMatrixSecurityRoleAssigner\bin\Release\` —
`BuMatrixSecurityRoleAssigner.dll` (the plugin) plus `BuMatrixSecurityRoleAssigner.Core.dll`
(its dependency, copied there automatically via the project reference).

If the XTB host is on 4.6.2, change `<TargetFramework>` to `net462` in both `.csproj` files.

> The XTB SDK package id is **`XrmToolBoxPackage`** on nuget.org, not `XrmToolBox.Extensibility`
> (that's the assembly/namespace it provides).

## Deploy

Install via XrmToolBox's own **Tool Library** (search "BU Matrix Security Role Assigner") — the
package is published on NuGet.org, no manual copying needed. To deploy a source build instead,
copy the plugin DLL **and** `BuMatrixSecurityRoleAssigner.Core.dll` into
`%AppData%\MscrmTools\XrmToolBox\Plugins`. Do **not** copy the SDK / XrmToolBox assemblies from
`bin` — the host ships those; copying them causes load conflicts.

To publish a new version to NuGet.org, use the `nuget-checklist-publish` skill rather than
running the workflow by hand — it validates the Tool Library checklist first and knows the
nuspec gotchas (icon path must use `/`, no custom `iconUrl`, `--skip-duplicate` silently no-ops
on an already-published version).

## Suggested next steps for the CLI

1. Consider a short `CHANGELOG.md`.

## Conventions

- Target Windows developers using Claude Code CLI and GitHub Copilot.
- No SDK assemblies shipped with the plugin; host provides them.
- Keep UI code in the `.Designer.cs`; logic in the control `.cs`.
- Prefer explicit, governed changes; don't guess entity/relationship names — the ones here
  (`team`, `systemuser`, `role`, `teamroles`, `systemuserroles`, `teamroles_association`,
  `parentrootroleid`, `businessunitid`, `teamtype`) are verified against the Dataverse schema.

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (`martintoelk/Modernized-BU-Security-Role-Assigner`), using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout — `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.

### NuGet checklist + publish

Validates the XrmToolBox Tool Library submission checklist, then publishes to NuGet.org.
User-invoked: `/nuget-checklist-publish <version>`. See
`.claude/skills/nuget-checklist-publish/SKILL.md`.
