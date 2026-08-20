# Modernized BU Role Assigner — XrmToolBox plugin

Handover context for Claude Code CLI. This file is the canonical project brief; read it
before touching code. Repo root on the author's machine: `D:\repos\Modernized BU Role Assigner`.

## What this is

An XrmToolBox plugin (C# / WinForms, .NET Framework 4.8) that bulk-assigns and removes
Dataverse **security roles** on **teams**. Two multi-select lists (teams | roles), the role
list shows each role's **business unit**, and two actions: *Add roles to team(s)* /
*Remove roles from team(s)*.

Built for the **modernized business units** (matrix data-access) model, where a team can hold
security roles from any BU — so the default behavior assigns the exact role selected, keeping
its BU. A legacy classic-BU code path exists, auto-detected via a behavioral probe (ticket #12)
rather than a manual toggle.

## Current status

Renamed and split into `BuMatrixSecurityRoleAssigner.Core` (class library, no WinForms/XTB
dependency) and `BuMatrixSecurityRoleAssigner` (the thin XTB plugin project) — see ticket #7.
Builds clean (`dotnet restore && dotnet build -c Release`, 0 errors). Manual smoke test against
a dev org is still outstanding.

## File map

| File | Purpose |
|------|---------|
| `BuMatrixSecurityRoleAssigner.Core/TeamRoleAssignmentService.cs` | Team/role data access + add/remove logic; depends only on `IOrganizationService` |
| `BuMatrixSecurityRoleAssigner.Core/Models.cs` | `TeamItem`, `RoleItem`, `OperationLog` |
| `BuMatrixSecurityRoleAssigner.Core/Generated/Entities/*.cs` | Early-bound Dataverse entity classes (`Team`, `Role`, `TeamRoles`, `SystemUser`, `SystemUserRoles`, `BusinessUnit`) — **generated, don't hand-edit**. Regenerate with `pac modelbuilder build --entitynamesfilter "team;role;systemuser;businessunit;teamroles;systemuserroles" --outdirectory "BuMatrixSecurityRoleAssigner.Core/Generated" --namespace "BuMatrixSecurityRoleAssigner.Core.Entities" --emitfieldsclasses` (ticket #14) |
| `BuMatrixSecurityRoleAssigner.Core/BuMatrixSecurityRoleAssigner.Core.csproj` | SDK-style class library (net48) |
| `BuMatrixSecurityRoleAssigner.Core.Tests/` | xUnit test project + hand-rolled `FakeOrganizationService` double (ticket #9) — no live org needed |
| `BuMatrixSecurityRoleAssigner/Plugin.cs` | MEF export + XTB metadata (plugin factory) |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.cs` | UI wiring + threading (`WorkAsync`), delegates logic to Core |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.Designer.cs` | WinForms UI (toolstrip, split lists, status bar) |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssigner.csproj` | SDK-style project (net48, `UseWindowsForms`), references Core |
| `BuMatrixSecurityRoleAssigner.slnx` | Solution referencing all three projects |
| `BuMatrixSecurityRoleAssigner.nuspec` | NuGet package spec (renamed to match, per ticket #6 prep) |
| `README.md` | End-user build/deploy/use notes |

## Architecture & key decisions

- **Early-bound entities.** `TeamRoleAssignmentService` uses the generated classes under
  `Generated/Entities` (`Team`, `Role`, `TeamRoles`, ...) instead of magic-string entity/attribute
  names — `Team.EntityLogicalName`, `Role.Fields.BusinessUnitId`, etc. (ticket #14).
- **N:N relationship** used for assignment is `teamroles_association` (intersect entity
  `teamroles`), via `IOrganizationService.Associate` / `Disassociate`.
- **Default = assign exact role.** The role the user selects is associated as-is, whatever its
  BU. Correct for modernized BUs; this is the intended primary path.
- **Classic path (auto-detected, ticket #12).** No manual toggle. The exact-role (modernized)
  association is tried first; if it faults for a team, that's treated as the signature of a
  classic-BU org and the role is resolved to the copy in the target team's BU, then retried
  once. Resolution key is `parentrootroleid`, which is identical across all BU copies of a
  logical role (`RootRoleId` in `RoleItem`; falls back to the role's own id for root-BU roles).
  A successful retry is surfaced via `OperationLog.ClassicBuDetected` (a warning, never a silent
  behavior switch). Teams with no copy of the role in their BU are skipped and reported under
  `NoRoleInBu`.
- **Idempotent.** Each team's current roles are read first (`GetTeamRoleIds`, link to
  `teamroles`), so add skips already-assigned pairs and remove skips not-assigned pairs — no
  duplicate-key errors, safe to re-run.
- **Access teams** can't hold security roles; the `Associate`/`Disassociate` error is caught
  per team and surfaced in the summary without aborting the batch.
- **Paging** on both loads (`PagingInfo`, 5000/page + paging cookie) so it won't truncate large
  orgs at 5k.
- **Threading** via `PluginControlBase.WorkAsync`; selections are read on the UI thread before
  the background work; progress via `SetWorkingMessage`.

## Open decisions (need author input)

1. ~~Keep or drop the classic-BU toggle?~~ Resolved in ticket #12: the manual `tsbMatchBu`
   toggle is gone, replaced by auto-detection (behavioral probe: try the exact-role Associate
   first, fall back to the team's own-BU copy on fault, and warn via `ClassicBuDetected`). The
   `byRootBu` resolution, `RoleItem.RootRoleId`, and `NoRoleInBu` stay — they're the fallback's
   resolution machinery, not toggle-only code.
2. ~~Rename to match the repo?~~ Done in ticket #7: `TeamRoleManager` →
   `BuMatrixSecurityRoleAssigner` across namespace, `AssemblyName`, `RootNamespace`, plugin
   `Name`/`Description` metadata, DLL filename, and the nuspec.
3. **Plugin tile icon.** `SmallImageBase64` / `BigImageBase64` are `null`. Add a 32px + 120px
   base64 PNG so it gets a proper tile in the XTB library.

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
> (that's the assembly/namespace it provides). Fixed in ticket #7 — the original `.csproj` had
> the wrong package id and could not restore.

## Deploy

Copy the plugin DLL **and** `BuMatrixSecurityRoleAssigner.Core.dll` into
`%AppData%\MscrmTools\XrmToolBox\Plugins`. Do **not** copy the SDK / XrmToolBox assemblies from
`bin` — the host ships those; copying them causes load conflicts. Restart XTB; the plugin
appears as "BU Matrix Security Role Assigner".

## Suggested next steps for the CLI

1. Smoke test on a dev org: load, multi-select teams + roles, add, verify via `teamroles`,
   remove, verify. Test an access team (expect a clean per-team error, not a crash) and a
   cross-BU assignment (expect success on modernized BUs).
2. Confirm Open decisions 1 and 3 with the author.
3. Consider: a plugin icon, and a short `CHANGELOG.md`.
4. A later ticket sets up a test project against `BuMatrixSecurityRoleAssigner.Core` using a
   fake `IOrganizationService` — the Core/UI split in ticket #7 is what makes that possible.

## Conventions

- Target Windows developers using Claude Code CLI and GitHub Copilot.
- No SDK assemblies shipped with the plugin; host provides them.
- Keep UI code in the `.Designer.cs`; logic in the control `.cs`.
- Prefer explicit, governed changes; don't guess entity/relationship names — the ones here
  (`team`, `role`, `teamroles`, `teamroles_association`, `parentrootroleid`, `businessunitid`,
  `teamtype`) are verified against the Dataverse schema.

## Agent skills

### Issue tracker

Issues live in this repo's GitHub Issues (`martintoelk/Modernized-BU-Security-Role-Assigner`), using the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/triage-labels.md`.

### Domain docs

Single-context layout — `CONTEXT.md` + `docs/adr/` at the repo root. See `docs/agents/domain.md`.
