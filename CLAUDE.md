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
its BU. A legacy classic-BU code path exists behind an opt-in toggle (see Open decisions).

## Current status

Working first cut, authored in a chat session and dropped into this repo. **Not yet compiled**
(it was written on Linux with no .NET Framework / XTB assemblies available). First job for the
CLI: restore, build, fix any compile errors, then a manual smoke test against a dev org.

## File map

| File | Purpose |
|------|---------|
| `Plugin.cs` | MEF export + XTB metadata (plugin factory) |
| `TeamRoleManagerControl.cs` | Data load + add/remove logic |
| `TeamRoleManagerControl.Designer.cs` | WinForms UI (toolstrip, split lists, status bar) |
| `Models.cs` | `TeamItem`, `RoleItem`, `OperationLog` |
| `TeamRoleManager.csproj` | SDK-style project (net48, `UseWindowsForms`) |
| `README.md` | End-user build/deploy/use notes |

> Namespace / assembly is still `TeamRoleManager` from the original draft. See Open decisions —
> decide whether to rename to match the repo (`ModernizedBuRoleAssigner`).

## Architecture & key decisions

- **N:N relationship** used for assignment is `teamroles_association` (intersect entity
  `teamroles`), via `IOrganizationService.Associate` / `Disassociate`.
- **Default = assign exact role.** The role the user selects is associated as-is, whatever its
  BU. Correct for modernized BUs; this is the intended primary path.
- **Classic path (opt-in toggle).** When enabled, each selected role is resolved to the copy in
  the target team's BU. Resolution key is `parentrootroleid`, which is identical across all BU
  copies of a logical role (`RootRoleId` in `RoleItem`; falls back to the role's own id for
  root-BU roles). Teams with no copy of the role in their BU are skipped and reported.
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

1. **Keep or drop the classic-BU toggle?** Author leans modernized-only, which would let us
   delete the toggle (`tsbMatchBu`), the `byRootBu` resolution, `RoleItem.RootRoleId`, the
   `parentrootroleid` column in `RetrieveRoles`, and the `NoRoleInBu` log bucket. Do **not**
   remove until confirmed — leaving it is low-cost insurance for any classic org.
2. **Rename to match the repo?** `TeamRoleManager` → `ModernizedBuRoleAssigner` across
   namespace, `AssemblyName`, `RootNamespace`, plugin `Name` metadata, and DLL filename. If yes,
   update `README.md` deploy filename too.
3. **Plugin tile icon.** `SmallImageBase64` / `BigImageBase64` are `null`. Add a 32px + 120px
   base64 PNG so it gets a proper tile in the XTB library.

## Build

Needs VS 2022 or `dotnet` SDK with the **.NET Framework 4.8** targeting pack + **.NET desktop**
workload (Windows only — WinForms + net48).

```
dotnet restore
dotnet build -c Release
```

Output: `bin\Release\TeamRoleManager.dll` (rename target if decision #2 is taken).

If the XTB host is on 4.6.2, change `<TargetFramework>` to `net462`.

## Deploy

Copy **only** the plugin DLL into `%AppData%\MscrmTools\XrmToolBox\Plugins`. Do **not** copy the
SDK / XrmToolBox assemblies from `bin` — the host ships those; copying them causes load conflicts.
Restart XTB; the plugin appears as "Team Role Manager" (or the renamed value).

## Suggested next steps for the CLI

1. `dotnet restore && dotnet build -c Release`; fix compile errors (watch the `1.*` floating
   `XrmToolBox.Extensibility` version — pin it once restore resolves a good one).
2. Confirm Open decisions 1–3 with the author, then apply.
3. Smoke test on a dev org: load, multi-select teams + roles, add, verify via `teamroles`,
   remove, verify. Test an access team (expect a clean per-team error, not a crash) and a
   cross-BU assignment (expect success on modernized BUs).
4. Consider: `.gitignore` (bin/obj), a plugin icon, and a short `CHANGELOG.md`.

## Conventions

- Target Windows developers using Claude Code CLI and GitHub Copilot.
- No SDK assemblies shipped with the plugin; host provides them.
- Keep UI code in the `.Designer.cs`; logic in the control `.cs`.
- Prefer explicit, governed changes; don't guess entity/relationship names — the ones here
  (`team`, `role`, `teamroles`, `teamroles_association`, `parentrootroleid`, `businessunitid`,
  `teamtype`) are verified against the Dataverse schema.
