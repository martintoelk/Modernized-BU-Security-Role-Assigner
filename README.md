# Modernized BU Security Role Assigner — XrmToolBox plugin

An XrmToolBox plugin to bulk-assign or remove Dataverse security **roles** on **teams** or
**users**, built specifically for orgs on the **modernized business units** (matrix
data-access) model. Unlike the classic BU model, where a role can only be associated with a
team/user in its own business unit, modernized BUs let a team or user hold security roles
from **any** business unit. This tool's default behavior takes advantage of that: it assigns
the exact role you selected, keeping its own BU, so you can freely assign roles from a
different BU than the team's or user's.

A legacy classic-BU compatibility path exists for orgs not yet on the modernized model,
auto-detected at run time rather than a manual toggle.

- **Mode: Teams / Mode: Users** toolbar toggle switches the left list between teams and users
  without mixing the two.
- Left list: every team (with **Business Unit** and **team type**) or every user (with
  **Business Unit** and **disabled** status), multi-select.
- Right list: every security role, with the **Business Unit** it belongs to (multi-select).
- **Add roles to team(s)/user(s)** / **Remove roles from team(s)/user(s)** buttons, plus a
  **Remove from all BUs** checkbox for the remove case (see [Business units](#business-units)).
- Quick text filter above each list.

## Roadmap

- [x] Bulk assign/remove roles on teams
- [x] Bulk assign/remove roles on users
- [x] Plugin tile icon
- [x] First verified build
- [x] Smoke test against a dev org

## Business units

**Default — modernized business units:** the plugin assigns the **exact role you selected**,
keeping whatever BU it belongs to. This is what you want on orgs with the modern matrix
data-access model, where a team or user can hold roles from any business unit. The BU column
on the role list is there so you pick the right copy.

**Classic model — auto-detected:** there's no manual toggle. The plugin always tries the exact
role you selected first. If that association faults for a team/user (the signature of a
classic-BU org, where a role can only be associated with a team/user in its own BU), it
retries with the copy of that role in the target's own BU (matched via `parentrootroleid`,
which is identical across all BU copies of a role) and reports it in the summary under a
"classic business-unit model detected" warning — never a silent behavior switch. Targets whose
BU has no copy of the role to fall back to are skipped and reported under "no matching role
copy in the target's business unit".

**Removing roles:** by default, only the exact role/BU pair(s) selected in the role list are
removed. Check **Remove from all BUs** to instead remove every business-unit copy of each
selected role currently assigned to the selected team(s)/user(s) — useful for cleaning up
after a classic-BU fallback assigned a different-BU copy than the one you'd select today.

Either way:
- **Access teams** cannot hold security roles; those teams are reported as errors (the run
  continues for the rest).
- Existing assignments are read first, so re-running is safe — already-assigned pairs are
  skipped on add, not-assigned pairs are skipped on remove.

## Install

Published on [NuGet.org](https://www.nuget.org/packages/BuMatrixSecurityRoleAssigner) as
`BuMatrixSecurityRoleAssigner`. The easiest way to get it is straight from XrmToolBox: open
**Tool Library**, search for **"BU Matrix Security Role Assigner"**, and install — no manual
DLL copying needed. To build and deploy from source instead, see Build/Deploy below.

## Build

Requires Visual Studio 2022 (or `dotnet` SDK) with the **.NET Framework 4.8** targeting pack
and the **.NET desktop development** workload.

```
dotnet restore
dotnet build -c Release
```

Output: `BuMatrixSecurityRoleAssigner\bin\Release\BuMatrixSecurityRoleAssigner.dll` (plus
`BuMatrixSecurityRoleAssigner.Core.dll`, which it depends on).

> If your XrmToolBox build still runs on .NET Framework 4.6.2, change `<TargetFramework>`
> in both `.csproj` files to `net462`.

## Deploy (from a source build)

Copy `BuMatrixSecurityRoleAssigner.dll` **and** `BuMatrixSecurityRoleAssigner.Core.dll` into
the XrmToolBox plugins folder:

```
%AppData%\MscrmTools\XrmToolBox\Plugins
```

Don't copy the SDK / XrmToolBox assemblies from `bin` — the host already ships those, and
copying them can cause version conflicts. Restart XrmToolBox; the plugin appears as
**BU Matrix Security Role Assigner**.

## Use

1. Open the plugin and connect to an environment.
2. Click **Load / Refresh**.
3. Optionally click the **Mode: Teams / Mode: Users** toggle to switch the left list.
4. Select one or more teams/users (left) and one or more roles (right).
5. For removal, optionally check **Remove from all BUs** (see [Business units](#business-units)).
6. Click **Add roles to team(s)/user(s)** or **Remove roles from team(s)/user(s)**.
7. Read the summary dialog.

## Files

| File | Purpose |
|------|---------|
| `BuMatrixSecurityRoleAssigner.Core/TeamRoleAssignmentService.cs` | Add/remove logic + team/user/role data access, depends only on `IOrganizationService` |
| `BuMatrixSecurityRoleAssigner.Core/Models.cs` | `TeamItem`, `UserItem`, `RoleItem`, `OperationLog`, `IAssignmentTarget` |
| `BuMatrixSecurityRoleAssigner.Core/BuMatrixSecurityRoleAssigner.Core.csproj` | Class library (net48), no WinForms/XTB dependency |
| `BuMatrixSecurityRoleAssigner/Plugin.cs` | XrmToolBox export/metadata (the plugin factory) |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.cs` | UI wiring, threading (`WorkAsync`), calls into Core |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssignerControl.Designer.cs` | WinForms UI |
| `BuMatrixSecurityRoleAssigner/BuMatrixSecurityRoleAssigner.csproj` | SDK-style project (net48, WinForms), references Core |

## License

[MIT](LICENSE)
