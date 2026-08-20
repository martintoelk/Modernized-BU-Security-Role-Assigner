# Modernized BU Security Role Assigner — XrmToolBox plugin

> **Preliminary / work in progress.** First cut of an XrmToolBox plugin for bulk-assigning
> and removing Dataverse security roles. Not yet built or smoke-tested — see
> [CLAUDE.md](CLAUDE.md) for full handover context and status.

An XrmToolBox plugin to bulk-assign or remove Dataverse security **roles** on **teams**
(users planned — see Roadmap), built specifically for orgs on the **modernized business
units** (matrix data-access) model. Unlike the classic BU model, where a role can only be
associated with a team/user in its own business unit, modernized BUs let a team or user hold
security roles from **any** business unit. This tool's default behavior takes advantage of
that: it assigns the exact role you selected, keeping its own BU, so you can freely assign
roles from a different BU than the team's.

A legacy classic-BU compatibility path exists behind an opt-in toggle for orgs not yet on
the modernized model.

- Left list: every team, with its **Business Unit** and **team type** (multi-select).
- Right list: every security role, with the **Business Unit** it belongs to (multi-select).
- **Add roles to team(s)** / **Remove roles from team(s)** buttons.
- Quick text filter above each list.

## Roadmap

- [x] Bulk assign/remove roles on teams
- [ ] Bulk assign/remove roles on users
- [ ] Plugin tile icon
- [ ] First verified build + smoke test against a dev org

## Business units

**Default — modernized business units:** the plugin assigns the **exact role you selected**,
keeping whatever BU it belongs to. This is what you want on orgs with the modern matrix
data-access model, where a team can hold roles from any business unit. The BU column on the
role list is there so you pick the right copy.

**Classic model — the "Match role BU to team (classic)" toggle:** turn this on for orgs still
on the classic BU model, where a role can only be associated with a team in its own BU. In that
mode, for each team the plugin resolves the copy of the selected role that lives in the team's
BU (matched via `parentrootroleid`, which is identical across all BU copies of a role). Teams
whose BU has no copy of the role are skipped and reported under "no matching role copy in the
team's business unit".

Either way:
- **Access teams** cannot hold security roles; those teams are reported as errors (the run
  continues for the rest).
- Existing assignments are read first, so re-running is safe — already-assigned pairs are
  skipped on add, not-assigned pairs are skipped on remove.

## Build

Requires Visual Studio 2022 (or `dotnet` SDK) with the **.NET Framework 4.8** targeting pack
and the **.NET desktop development** workload.

```
dotnet restore
dotnet build -c Release
```

Output: `bin/Release/TeamRoleManager.dll`.

> If your XrmToolBox build still runs on .NET Framework 4.6.2, change `<TargetFramework>`
> in `TeamRoleManager.csproj` to `net462`.

## Deploy

Copy **only** `TeamRoleManager.dll` into the XrmToolBox plugins folder:

```
%AppData%\MscrmTools\XrmToolBox\Plugins
```

Don't copy the SDK / XrmToolBox assemblies from `bin` — the host already ships those, and
copying them can cause version conflicts. Restart XrmToolBox; the plugin appears as
**Team Role Manager**.

## Use

1. Open the plugin and connect to an environment.
2. Click **Load / Refresh**.
3. Select one or more teams (left) and one or more roles (right).
4. Click **Add roles to team(s)** or **Remove roles from team(s)**.
5. Read the summary dialog.

## Files

| File | Purpose |
|------|---------|
| `Plugin.cs` | XrmToolBox export/metadata (the plugin factory) |
| `TeamRoleManagerControl.cs` | Data loading + add/remove logic |
| `TeamRoleManagerControl.Designer.cs` | WinForms UI |
| `Models.cs` | `TeamItem`, `RoleItem`, `OperationLog` |
| `TeamRoleManager.csproj` | SDK-style project (net48, WinForms) |
