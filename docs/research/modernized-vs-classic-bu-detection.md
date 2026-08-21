# Detecting Modernized (Matrix) Business Units vs. Classic BU Model at Runtime

## Summary / Recommendation

There is no ordinary entity attribute on `organization` (or anywhere else) that exposes
"matrix data access model enabled" as a normal boolean column you can put in a `ColumnSet`.
The feature switch is implemented as an **OrgDBOrgSetting** named
**`EnableOwnershipAcrossBusinessUnits`**, which Microsoft's own docs say is set via the
OrgDBOrgSettings tool (or, in the modern PPAC UI, the **Record ownership across business
units** toggle under *Environment → Settings → Product → Features*). OrgDBOrgSettings are
not first-class attributes — they are packed into a single hidden system attribute called
**`orgdborgsettings`** on the `organization` table, which stores an XML blob of all
OrgDBOrgSettings key/value pairs for the org. That attribute *is* `IsValidForRead = true`
in the entity metadata, so it is technically retrievable through `IOrganizationService`/Web
API — but only for callers with `prvReadOrganization`-level (effectively System
Administrator / System Customizer) privileges, and Microsoft does not publish a supported
contract for parsing it, so treat this as a best-effort/undocumented read, not a guaranteed
API. **Recommended approach for this plugin:** read `orgdborgsettings` from the
`organization` entity and look for `EnableOwnershipAcrossBusinessUnits` (case-insensitive,
value `1`/`true`) in the XML; if the read fails (privilege, throttling, or the blob shape
changes) or the key is absent, fall back to the safe default of the toggle being **off**
(classic behavior) and let the user override via the existing `tsbMatchBu` opt-in toggle —
never silently auto-switch behavior on an unparsed/ambiguous result. Because a wrong
guess here only affects which convenience default is pre-selected in the UI (the user's
explicit selection always wins), the reliability of the XML-scrape is an acceptable risk
for this feature; do not use it to gate anything security-critical.

## Findings

### 1. No plain `organization` attribute for this flag

The published `organization` entity attribute list (Microsoft Learn "Organization
table/entity reference") has no attribute named anything like `matrixdataaccessmodel`,
`businessunitmatrixmodel`, `modernizedbusinessunits`, or similar. The feature is exposed to
admins purely as a PPAC toggle labeled **Record ownership across business units**
(Environment → Settings → Product → Features), and to scripts/tools via OrgDBOrgSettings —
never as a dedicated queryable column.

### 2. It is an OrgDBOrgSetting, packed into `orgdborgsettings`

Per "Security concepts in Microsoft Dataverse" (`wp-security-cds`):

> "This feature switch is stored in the **EnableOwnershipAcrossBusinessUnits** setting and
> can be set using the [OrgDBOrgSettings tool for Microsoft Dynamics CRM]."

Two related settings also live in OrgDBOrgSettings and are relevant to the "modernized"
posture of an org:

- `EnableOwnershipAcrossBusinessUnits` — the master switch (this is the "matrix / modernized
  BU" flag).
- `RecomputeOwnershipAcrossBusinessUnits` — one-shot recompute trigger used when turning the
  feature on (locks the system briefly while it runs).
- `AlwaysMoveRecordToOwnerBusinessUnit` — must be `false` for cross-BU record ownership to
  stick; if `true`, records snap back to the owner's own BU.

OrgDBOrgSettings as a whole are **not individual SDK entities or attributes** — historically
(on-prem CRM) they lived in a `MSCRM_config` database table; in Dataverse online they are
serialized into the `organization.orgdborgsettings` string attribute as an XML blob (see the
Common Data Model schema reference for `orgDbOrgSettings`: "Organization settings stored in
Organization Database", type `string`, max length ~1GB, `IsValidForRead = True`,
`IsValidForForm = False`). Because `IsValidForForm = false`, it never shows up on any form,
but it is still retrievable via `IOrganizationService.Retrieve`/`RetrieveMultiple` with an
explicit `ColumnSet` naming it — Microsoft does not appear to hide it from the SDK the way
some truly-internal columns are hidden, but reading it does require elevated privileges (it
is treated as organization-configuration data, effectively requiring the
`prvReadOrganization` privilege that comes with System Administrator / System Customizer).
There is no documented, supported schema for the XML content — Microsoft's own tooling
(the OrgDBOrgSettings/Organization Settings Editor utilities) is what parses/writes it, and
the exact tag names are only evidenced indirectly through support/docs articles, not a
published XSD.

C# sketch (IOrganizationService), with defensive parsing and privilege-failure fallback:

```csharp
private bool? TryDetectModernizedBusinessUnits(IOrganizationService svc)
{
    try
    {
        // There is exactly one organization row; retrieve it via WhoAmI + Retrieve,
        // or query it directly.
        var orgs = svc.RetrieveMultiple(new QueryExpression("organization")
        {
            ColumnSet = new ColumnSet("orgdborgsettings")
        }).Entities;

        if (orgs.Count == 0) return null;

        var xml = orgs[0].GetAttributeValue<string>("orgdborgsettings");
        if (string.IsNullOrWhiteSpace(xml)) return false; // no overrides set -> default off

        // OrgDBOrgSettings XML is an undocumented shape; parse defensively.
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var node = doc.Descendants("EnableOwnershipAcrossBusinessUnits").FirstOrDefault();
        if (node == null) return false; // key absent -> feature not enabled (default)

        return string.Equals(node.Value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.Value, "true", StringComparison.OrdinalIgnoreCase);
    }
    catch (Exception)
    {
        // Insufficient privilege, throttling, or a future shape change in the blob.
        // Do not guess — let the caller fall back to the manual toggle.
        return null;
    }
}
```

Callers should treat `null` as "unknown — ask the user / keep current UI default," `true`
as "pre-select modernized/exact-role-BU behavior," and `false` as "pre-select classic
toggle." This mirrors the plugin's existing opt-in `tsbMatchBu` toggle, which should remain
the ultimate source of truth regardless of what this detection returns.

### 3. Not admin-API-only

Unlike some environment-level flags (e.g. Dataverse-for-Teams provisioning, some governance
settings), this one is *not* exclusively behind the Power Platform Admin API/PPAC — it is
persisted inside the Dataverse organization database itself (`orgdborgsettings`), so it is
in principle reachable from inside the org via `IOrganizationService`/Web API, just not as a
first-class attribute and not with a published parsing contract. There is no evidence of an
equivalent flag being exposed through `Get-AdminPowerAppEnvironment` or other
`Microsoft.PowerApps.Administration.PowerShell` cmdlets — those surface environment
provisioning/lifecycle properties, not this security-model feature switch.

### 4. Fallback heuristic (recommended as the primary safety net, not just a backup)

Given the XML-scrape above is undocumented and privilege-gated, prefer a **behavioral probe**
as the more robust signal, especially for a tool that already needs
`prvCreateTeamRoles`/`prvReadRole` etc. to function at all:

- **Probe:** Attempt to `Associate` a security role whose `businessunitid` differs from the
  target team's `businessunitid` (e.g., a small internal no-op test, or simply observe the
  result of the user's first real cross-BU add operation). In a classic-BU org, Dataverse
  rejects associating a role from a different BU to a team with a platform error
  (unsupported/invalid association); in a modernized/matrix org, the association succeeds
  with the role's own BU preserved on the `teamroles` row.
- Because this plugin is explicitly designed so its **default** codepath already assigns the
  exact role as selected (correct for modernized BUs) and only falls back to same-BU
  resolution when the user opts into the classic toggle, the cheapest and safest heuristic is
  simply: *try the direct/default path first; if `Associate` throws a
  BU-mismatch/unsupported-association fault for a given team, treat that org as
  classic-BU-scoped for that team and prompt the user to enable `tsbMatchBu` instead of
  guessing up front.* This avoids needing any privileged read and is self-correcting per
  team/org without a fragile XML parse.
- A secondary, lower-signal heuristic: query whether any `role` records in the org have
  `businessunitid` values other than the root BU that also share a `parentrootroleid`
  pointing to a role in a *different* BU than their own root copy — i.e., whether BU-scoped
  "copies" of roles exist at all. Classic orgs auto-create a per-BU copy of every root-BU
  security role; modernized-only orgs that were "born modernized" may not. This is weaker
  evidence (it reflects role-copy history, not the live feature switch) and should not be
  relied on alone.

## Minimum SDK/API version and privilege notes

- Matrix data access (modernized business units) was announced as a preview capability in the
  **2021 release wave 2** Power Platform release notes and later reached general
  availability; no specific SDK/Web API version gate is documented for reading
  `orgdborgsettings` itself — it has existed as a Dataverse/CDS `organization` attribute for
  a long time (predates the matrix-BU feature), so no `api-version` floor is needed beyond
  whatever the plugin already targets.
- Reading `orgdborgsettings` requires a privilege level equivalent to System Administrator /
  System Customizer (organization-level read on configuration data); a normal end-user
  security role will typically get an `Retrieve`/`RetrieveMultiple` fault or simply an empty
  value. XrmToolBox plugins commonly run as an admin-connected user, so this is usually fine,
  but should not be assumed — hence the `try/catch → null` fallback above.
- No separate "matrix data access API version" gate was found; the feature is controlled
  purely by the OrgDBOrgSetting plus (for record-ownership recompute) the one-shot
  `RecomputeOwnershipAcrossBusinessUnits`/`AlwaysMoveRecordToOwnerBusinessUnit` settings
  described above.

## Sources

- https://learn.microsoft.com/power-platform/admin/wp-security-cds — primary source for:
  the "Matrix data access structure (Modernized Business Units)" concept, the "Enable the
  Matrix data access structure" admin steps (PPAC toggle path), the statement that "This
  feature switch is stored in the **EnableOwnershipAcrossBusinessUnits** setting and can be
  set using the OrgDBOrgSettings tool," and the `RecomputeOwnershipAcrossBusinessUnits` /
  `AlwaysMoveRecordToOwnerBusinessUnit` settings under "Record Ownership in Modernized
  Business Units."
- https://learn.microsoft.com/power-platform/admin/modernized-business-units-security —
  conceptual description of modernized BUs, how owning-BU-based access works, and that Teams
  ownership is no longer required to share cross-BU (used for the summary framing).
- https://learn.microsoft.com/power-apps/developer/data-platform/reference/entities/organization
  — authoritative attribute list for the `organization` entity, confirming there is no
  dedicated "matrix/modernized BU" boolean attribute, and confirming the `orgdborgsettings`
  attribute's metadata (`LogicalName: orgdborgsettings`, `Type: String`,
  `IsValidForRead: True`, `IsValidForForm: False`, `MaxLength: 1073741823`).
- https://learn.microsoft.com/common-data-model/schema/core/applicationcommon/organization
  and the `foundationCommon`/`scheduling` CDM variants of the same page — corroborate the
  `orgDbOrgSettings` attribute shape/description ("Organization settings stored in
  Organization Database") independent of the entity-reference page, supporting that this is
  the one column that packs OrgDBOrgSettings values.
- https://learn.microsoft.com/power-platform/admin/orgdborgsettings — confirms OrgDBOrgSettings
  are a general mechanism (documented in detail for server-side-sync settings) configured via
  the OrgDBOrgSettings tool / Organization Settings editor, i.e. not a normal attribute-level
  feature.
- https://support.microsoft.com/help/2691237/orgdborgsettings-tool-for-microsoft-dynamics-crm
  — referenced repeatedly by the wp-security-cds page as the tool used to read/set
  OrgDBOrgSettings including `EnableOwnershipAcrossBusinessUnits`.
- https://learn.microsoft.com/power-platform/admin/update-record-owner — corroborates that
  cross-BU record ownership behavior is gated by "allow record ownership across business
  units" (the same PPAC toggle/OrgDBOrgSetting), and shows a second, related OrgDBOrgSetting
  (`allowRoleAssignmentOnDisabledUsers`) that is read/set through the same undocumented
  mechanism, supporting the "not a normal attribute" conclusion.
- https://learn.microsoft.com/dynamics365/customer-insights/journeys/real-time-marketing-modernized-business-units
  — secondary corroboration that "modernized business units" is the officially used feature
  name and that it is "turned on" via the same `wp-security-cds#enable-the-matrix-data-access-structure`
  toggle, plus links to the original 2021 release-wave-2 announcement
  (https://learn.microsoft.com/power-platform-release-plan/2021wave2/data-platform/modernize-business-units)
  used to date the feature's introduction.
