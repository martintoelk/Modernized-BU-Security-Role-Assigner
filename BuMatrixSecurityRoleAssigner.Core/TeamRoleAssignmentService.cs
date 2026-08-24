using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using BuMatrixSecurityRoleAssigner.Core.Entities;

namespace BuMatrixSecurityRoleAssigner.Core
{
    /// <summary>
    /// Team/role data access and the add/remove logic. Depends only on IOrganizationService,
    /// so it can be exercised in a unit test against a fake service - no WinForms, no
    /// PluginControlBase/WorkAsync. Uses the early-bound entity classes under
    /// Generated/Entities (regenerate via `pac modelbuilder build` - see that folder's README)
    /// instead of magic-string attribute/entity names.
    /// </summary>
    public class TeamRoleAssignmentService
    {
        /// <summary>
        /// How many roles go into one Associate/Disassociate call, and so also how often progress
        /// is reported within a single target (target boundaries always report). Chunking the
        /// platform call is what lets progress move inside a target instead of only at target
        /// boundaries; 10 is small enough that a long run feels alive, and large enough that the
        /// extra round trips stay a rounding error next to the per-role work the platform does
        /// anyway.
        /// </summary>
        public const int RoleBatchSize = 10;

        private readonly IOrganizationService _service;

        public TeamRoleAssignmentService(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public List<TeamItem> RetrieveTeams(bool ignorePowerVirtualAgentTeams = true)
        {
            var query = new QueryExpression(Team.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Team.Fields.Name, Team.Fields.BusinessUnitId, Team.Fields.TeamType,
                    Team.Fields.Description),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            // Access teams can't hold security roles, so they're never valid targets - exclude
            // them at the query level rather than offering them as selectable and relying solely
            // on the per-target Associate/Disassociate fault catch (which stays as a fallback for
            // any other target type that legitimately can't hold a role).
            // team_type.Zugreifen (org language is German) is the generated name for the "Access" team type.
            query.Criteria.AddCondition(Team.Fields.TeamType, ConditionOperator.NotEqual, (int)team_type.Zugreifen);
            if (ignorePowerVirtualAgentTeams)
            {
                // DoesNotContain is present in the SDK enum but is rejected by some Dataverse
                // organizations as an unknown QueryExpression operator. NotLike is the supported
                // QueryExpression string operator; keep rows with no description explicitly because
                // SQL-style NOT LIKE does not match null values.
                query.Criteria.AddFilter(new FilterExpression(LogicalOperator.Or)
                {
                    Conditions =
                    {
                        new ConditionExpression(Team.Fields.Description, ConditionOperator.Null),
                        new ConditionExpression(Team.Fields.Description, ConditionOperator.NotLike,
                            "%power virtual agents%")
                    }
                });
            }
            query.AddOrder(Team.Fields.Name, OrderType.Ascending);

            var list = new List<TeamItem>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var t in ec.Entities.Select(e => e.ToEntity<Team>()))
                {
                    var bu = t.BusinessUnitId;
                    list.Add(new TeamItem
                    {
                        Id = t.Id,
                        Name = t.Name,
                        BusinessUnitId = bu?.Id ?? Guid.Empty,
                        BusinessUnitName = bu?.Name ?? string.Empty,
                        TeamType = t.FormattedValues.ContainsKey(Team.Fields.TeamType)
                            ? t.FormattedValues[Team.Fields.TeamType]
                            : string.Empty
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list;
        }

        /// <summary>
        /// Users mode: analogous to <see cref="RetrieveTeams"/>. Disabled users are included, not
        /// excluded - flagged via <see cref="UserItem.IsDisabled"/> so the caller can warn rather
        /// than silently letting a role get assigned to a disabled account.
        /// </summary>
        public List<UserItem> RetrieveUsers()
        {
            var query = new QueryExpression(SystemUser.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(SystemUser.Fields.FullName, SystemUser.Fields.BusinessUnitId, SystemUser.Fields.IsDisabled),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.AddOrder(SystemUser.Fields.FullName, OrderType.Ascending);

            var list = new List<UserItem>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var u in ec.Entities.Select(e => e.ToEntity<SystemUser>()))
                {
                    var bu = u.BusinessUnitId;
                    list.Add(new UserItem
                    {
                        Id = u.Id,
                        Name = u.FullName,
                        BusinessUnitId = bu?.Id ?? Guid.Empty,
                        BusinessUnitName = bu?.Name ?? string.Empty,
                        IsDisabled = u.IsDisabled ?? false
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list;
        }

        public List<RoleItem> RetrieveRoles()
        {
            var query = new QueryExpression(Role.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Role.Fields.Name, Role.Fields.BusinessUnitId, Role.Fields.ParentRootRoleId),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.AddOrder(Role.Fields.Name, OrderType.Ascending);

            var list = new List<RoleItem>();
            EntityCollection ec;
            do
            {
                ec = _service.RetrieveMultiple(query);
                foreach (var r in ec.Entities.Select(e => e.ToEntity<Role>()))
                {
                    var bu = r.BusinessUnitId;
                    var root = r.ParentRootRoleId;
                    list.Add(new RoleItem
                    {
                        Id = r.Id,
                        Name = r.Name,
                        BusinessUnitId = bu?.Id ?? Guid.Empty,
                        BusinessUnitName = bu?.Name ?? string.Empty,
                        // For a role in the root BU, parentrootroleid points to itself; fall back to own id.
                        RootRoleId = root?.Id ?? r.Id
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            // The query only orders by name server-side; business unit is a lookup, so ordering
            // by its attribute would sort by the underlying id rather than the displayed BU name.
            // Sort client-side instead, so every BU copy of a role sorts consistently by BU name.
            return list
                .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.BusinessUnitName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Informational only - reads the undocumented <c>EnableOwnershipAcrossBusinessUnits</c>
        /// OrgDBOrgSetting from <c>organization.orgdborgsettings</c> to report whether the
        /// connected org has modernized (matrix) business units switched on. There is no
        /// generated early-bound entity for <c>organization</c> (an internal/undocumented column,
        /// unlike the modeled entities used elsewhere in this class), so this queries it directly.
        /// Never wired into add/remove: the behavioral probe in <see cref="AssignOrRemove"/> stays
        /// authoritative for that, per docs/research/modernized-vs-classic-bu-detection.md.
        /// Degrades to <see cref="ModernizedBuStatus.Unknown"/> rather than throwing - the read
        /// requires elevated (System Administrator/System Customizer) privilege and the XML shape
        /// is undocumented, so any failure here should never block Load.
        /// </summary>
        public ModernizedBuStatus GetModernizedBuStatus()
        {
            try
            {
                var query = new QueryExpression("organization")
                {
                    ColumnSet = new ColumnSet("orgdborgsettings"),
                    PageInfo = new PagingInfo { Count = 1, PageNumber = 1 }
                };
                var org = _service.RetrieveMultiple(query).Entities.FirstOrDefault();
                if (org == null)
                    return ModernizedBuStatus.Unknown;

                var xml = org.GetAttributeValue<string>("orgdborgsettings");
                if (string.IsNullOrWhiteSpace(xml))
                    return ModernizedBuStatus.No;

                // Match by local name, not a namespace-qualified XName: the blob's shape is
                // undocumented, and a default xmlns on its root would otherwise make Descendants(string)
                // silently find nothing.
                var value = XDocument.Parse(xml).Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "EnableOwnershipAcrossBusinessUnits")?.Value?.Trim();
                // OrgDBOrgSettings booleans appear as either "true" or "1" (see
                // docs/research/modernized-vs-classic-bu-detection.md) - accept both.
                return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1"
                    ? ModernizedBuStatus.Yes
                    : ModernizedBuStatus.No;
            }
            catch (Exception)
            {
                // Privilege fault, throttling, or a future shape change in the undocumented blob -
                // never let this fail the whole Load.
                return ModernizedBuStatus.Unknown;
            }
        }

        /// <summary>Role ids (BU-specific) currently associated with the given team.</summary>
        public HashSet<Guid> GetTeamRoleIds(Guid teamId)
        {
            var query = new QueryExpression(Role.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Role.Fields.RoleId)
            };
            var link = query.AddLink(TeamRoles.EntityLogicalName, Role.Fields.RoleId, TeamRoles.Fields.RoleId);
            link.LinkCriteria.AddCondition(TeamRoles.Fields.TeamId, ConditionOperator.Equal, teamId);

            var result = _service.RetrieveMultiple(query);
            return new HashSet<Guid>(result.Entities.Select(e => e.Id));
        }

        /// <summary>Role ids (BU-specific) currently associated with the given user.</summary>
        public HashSet<Guid> GetUserRoleIds(Guid userId)
        {
            var query = new QueryExpression(Role.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Role.Fields.RoleId)
            };
            var link = query.AddLink(SystemUserRoles.EntityLogicalName, Role.Fields.RoleId, SystemUserRoles.Fields.RoleId);
            link.LinkCriteria.AddCondition(SystemUserRoles.Fields.SystemUserId, ConditionOperator.Equal, userId);

            var result = _service.RetrieveMultiple(query);
            return new HashSet<Guid>(result.Entities.Select(e => e.Id));
        }

        private HashSet<Guid> GetExistingRoleIds(IAssignmentTarget target) =>
            target.EntityLogicalName == Team.EntityLogicalName
                ? GetTeamRoleIds(target.Id)
                : GetUserRoleIds(target.Id);

        /// <summary>
        /// Assigns or removes <paramref name="selectedRoles"/> for each of <paramref name="targets"/>
        /// - teams or users, not mixed within one call (mode is decided by the caller).
        /// Default (modernized business units): the exact role selected is associated as-is,
        /// keeping its own business unit. Classic-BU orgs/teams are auto-detected via a
        /// behavioral probe, not a manual toggle: if the exact-role association faults for a
        /// team, each faulted role is resolved to the copy that lives in that team's own
        /// business unit (using <paramref name="allRoles"/> - every role, not just the
        /// selection - to build the root-role -> BU -> role index) and retried once. A
        /// successful retry is surfaced as a warning (<see cref="OperationLog.ClassicBuDetected"/>),
        /// never a silent behavior switch; if the retry can't resolve a BU copy or also faults,
        /// the original error/skip is reported as before.
        /// <para>
        /// Remove-only, default off: <paramref name="removeFromAllBus"/>. Normal remove only touches
        /// the exact role row(s) selected (one BU copy per selection). When on, each selected role is
        /// widened to every BU copy sharing its <see cref="RoleItem.RootRoleId"/> and every copy
        /// currently assigned to the team is removed, not just the row picked in the list - a wider
        /// blast radius, so the caller should confirm first.
        /// </para>
        /// <para>
        /// <paramref name="progress"/> is reported in role units - see
        /// <see cref="AssignRemoveProgress"/> - and is throttled to roughly one report per
        /// <see cref="RoleBatchSize"/> units.
        /// </para>
        /// </summary>
        public OperationLog AssignOrRemove(
            IReadOnlyList<IAssignmentTarget> targets,
            IReadOnlyList<RoleItem> selectedRoles,
            IReadOnlyList<RoleItem> allRoles,
            bool add,
            bool removeFromAllBus = false,
            Action<AssignRemoveProgress> progress = null)
        {
            if (targets == null) throw new ArgumentNullException(nameof(targets));
            if (selectedRoles == null) throw new ArgumentNullException(nameof(selectedRoles));
            if (allRoles == null) throw new ArgumentNullException(nameof(allRoles));

            // Index every role copy by (root role, business unit), for the classic-BU fallback.
            var byRootBu = allRoles.GroupBy(r => r.RootRoleId)
                                    .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.BusinessUnitId, r => r));

            // Only needed for "remove from all BUs": every role copy sharing a root role, as a list.
            var byRootAll = !add && removeFromAllBus
                ? allRoles.GroupBy(r => r.RootRoleId).ToDictionary(g => g.Key, g => g.ToList())
                : null;

            var log = new OperationLog();

            // Modernized default: assign/remove exactly what the user picked, whatever its BU -
            // unless removeFromAllBus widens each selection to every BU copy of that logical role.
            // Neither the selection nor the widening depends on the target, so this is computed
            // once and every target works through the same rows.
            var rolesPerTarget = WidenSelection(selectedRoles, byRootAll);

            // Progress is counted in role units - see AssignRemoveProgress for why targets alone
            // make too coarse a denominator.
            var totalUnits = rolesPerTarget.Count * targets.Count;
            var unitsDone = 0;
            var targetsDone = 0;
            var lastReportedUnits = -RoleBatchSize;  // so the opening report always fires
            var currentTargetName = string.Empty;

            void Report(bool force = false)
            {
                if (progress == null)
                    return;
                if (!force && unitsDone - lastReportedUnits < RoleBatchSize)
                    return;
                lastReportedUnits = unitsDone;
                progress(new AssignRemoveProgress(unitsDone, totalUnits, targetsDone, targets.Count, currentTargetName));
            }

            void ProcessTarget(IAssignmentTarget target)
            {
                if (add && target is UserItem { IsDisabled: true })
                    log.DisabledUserWarnings.Add(target.Name);

                HashSet<Guid> existing;
                try
                {
                    existing = GetExistingRoleIds(target);
                }
                catch (Exception ex)
                {
                    log.Errors.Add($"{target.Name}: could not read existing roles ({ex.Message})");
                    return;
                }

                var roleTargets = new List<RoleItem>();
                foreach (var row in rolesPerTarget)
                {
                    if (add)
                    {
                        if (existing.Contains(row.Id))
                            log.AlreadyPresent.Add($"{target.Name} <- {row.Name}");
                        else
                            roleTargets.Add(row);
                    }
                    else
                    {
                        if (!existing.Contains(row.Id))
                            log.NotPresent.Add($"{target.Name} <- {row.Name}");
                        else
                            roleTargets.Add(row);
                    }
                }

                // Rows already in the wanted state need no platform call, but they're still work
                // this run accounted for - credit them or the bar stalls short of the total.
                unitsDone += rolesPerTarget.Count - roleTargets.Count;

                // One classic-BU warning and one error line per target, not per batch: the summary
                // counts targets ("detected for N team(s)"), and a target that can't hold roles at
                // all fails every one of its batches with the same message.
                var classicBuResolved = 0;
                var targetErrors = new List<string>();

                foreach (var batch in InBatches(roleTargets, RoleBatchSize))
                {
                    classicBuResolved += ApplyRoleBatch(target, batch, add, existing, byRootBu, log, targetErrors);
                    unitsDone += batch.Count;
                    Report();
                }

                if (classicBuResolved > 0)
                    log.ClassicBuDetected.Add(
                        $"{target.Name} ({target.BusinessUnitName}): matched {classicBuResolved} role(s) to the target's own business unit.");

                foreach (var message in targetErrors)
                    log.Errors.Add($"{target.Name}: {message}");
            }

            foreach (var target in targets)
            {
                currentTargetName = target.Name;
                var unitsBeforeTarget = unitsDone;
                // A target boundary always reports, throttle or not. Throttling these too would
                // starve any run whose targets are worth fewer than RoleBatchSize units each -
                // 9 teams x 1 role would sit at 0% with no ETA from start to finish, which is
                // worse than the per-target reporting this replaced.
                Report(force: true);

                ProcessTarget(target);

                // Whatever ProcessTarget managed to do - including bailing out early - the target's
                // units are all settled once it returns.
                unitsDone = unitsBeforeTarget + rolesPerTarget.Count;
                targetsDone++;
            }

            Report(force: true);
            return log;
        }

        /// <summary>
        /// The distinct role rows each target will be worked through: the selection exactly as
        /// picked, or - when "remove from all BUs" is on (<paramref name="byRootAll"/> non-null) -
        /// every BU copy sharing each selection's root role. Deduped, because two selected rows can
        /// widen onto the same copy (e.g. two BU copies of one logical role both selected).
        /// </summary>
        private static List<RoleItem> WidenSelection(
            IReadOnlyList<RoleItem> selectedRoles,
            Dictionary<Guid, List<RoleItem>> byRootAll)
        {
            var widened = new List<RoleItem>();
            var seen = new HashSet<Guid>();
            foreach (var role in selectedRoles)
            {
                var rows = byRootAll != null && byRootAll.TryGetValue(role.RootRoleId, out var copies)
                    ? (IReadOnlyList<RoleItem>)copies
                    : new[] { role };

                foreach (var row in rows)
                {
                    if (seen.Add(row.Id))
                        widened.Add(row);
                }
            }
            return widened;
        }

        /// <summary>Splits a list into consecutive batches of at most <paramref name="size"/> items.</summary>
        private static IEnumerable<List<T>> InBatches<T>(IReadOnlyList<T> items, int size)
        {
            for (var start = 0; start < items.Count; start += size)
            {
                var batch = new List<T>(Math.Min(size, items.Count - start));
                for (var i = start; i < start + size && i < items.Count; i++)
                    batch.Add(items[i]);
                yield return batch;
            }
        }

        /// <summary>
        /// (Dis)associates one batch of roles for one target, with the classic-BU probe/retry
        /// around it. Returns how many roles the same-BU fallback resolved - 0 when the batch went
        /// through exactly as selected, which is the modernized-BU case. Error messages are
        /// appended to <paramref name="targetErrors"/> (deduped) for the caller to report once per
        /// target rather than once per batch.
        /// </summary>
        private int ApplyRoleBatch(
            IAssignmentTarget target,
            List<RoleItem> batch,
            bool add,
            HashSet<Guid> existing,
            Dictionary<Guid, Dictionary<Guid, RoleItem>> byRootBu,
            OperationLog log,
            List<string> targetErrors)
        {
            void AddError(string message)
            {
                if (!targetErrors.Contains(message))
                    targetErrors.Add(message);
            }

            try
            {
                AssociateOrDisassociate(target, batch.Select(r => r.ToRef()), add);
                log.Changed += batch.Count;
                MarkApplied(existing, batch, add);
                return 0;
            }
            catch (Exception)
            {
                // Probe result: either a genuine per-target fault (e.g. Access teams can't hold
                // security roles, or an anti-elevation check on user role assignment) or a
                // classic-BU mismatch. Retry resolving each faulted role to this target's own BU
                // copy; a successful retry confirms classic-BU behavior. Roles split three ways:
                //  - resolved: a distinct BU copy exists and isn't already in the state we
                //    want (add: not yet assigned; remove: currently assigned) - worth retrying.
                //  - noBuCopy: no copy of this logical role exists in this target's BU at all -
                //    a genuine "nothing to fall back to", reported as NoRoleInBu.
                //  - sameBu: the "closest" copy IS the one we already tried (target is already in
                //    that role's BU) - the fault isn't a BU mismatch, so it's a real error
                //    (e.g. Access team, or an anti-elevation rejection), not a BU-copy gap.
                // The BU-copy's own id (not the originally-selected role's id) is what's checked
                // against `existing` here - it's the actual row that would be (dis)associated,
                // and may already be in the wanted state from a prior run (idempotency).
                var resolved = new List<RoleItem>();
                var resolvedIds = new HashSet<Guid>();
                var noBuCopy = new List<RoleItem>();
                var sameBu = new List<RoleItem>();
                foreach (var role in batch)
                {
                    if (!byRootBu.TryGetValue(role.RootRoleId, out var buMap) ||
                        !buMap.TryGetValue(target.BusinessUnitId, out var buCopy))
                    {
                        noBuCopy.Add(role);
                        continue;
                    }

                    if (buCopy.Id == role.Id)
                    {
                        sameBu.Add(role);
                        continue;
                    }

                    // Two selected BU copies of one logical role resolve onto the same same-BU
                    // copy - queue it once, or the retry would (dis)associate a duplicate row and
                    // fault on it.
                    if (!resolvedIds.Add(buCopy.Id))
                        continue;

                    if (add)
                    {
                        if (existing.Contains(buCopy.Id))
                            log.AlreadyPresent.Add($"{target.Name} <- {buCopy.Name}");
                        else
                            resolved.Add(buCopy);
                    }
                    else
                    {
                        if (!existing.Contains(buCopy.Id))
                            log.NotPresent.Add($"{target.Name} <- {buCopy.Name}");
                        else
                            resolved.Add(buCopy);
                    }
                }

                foreach (var role in noBuCopy)
                    log.NoRoleInBu.Add($"{target.Name} ({target.BusinessUnitName}) <- {role.Name}");

                // sameBu roles shared the failed batch with roles that may genuinely have needed
                // BU resolution, so the batch fault doesn't necessarily implicate them - retry
                // them alone to tell a real per-role error apart from collateral batch failure.
                if (sameBu.Count > 0)
                {
                    try
                    {
                        AssociateOrDisassociate(target, sameBu.Select(r => r.ToRef()), add);
                        log.Changed += sameBu.Count;
                        MarkApplied(existing, sameBu, add);
                    }
                    catch (Exception exSameBu)
                    {
                        AddError(exSameBu.Message);
                    }
                }

                if (resolved.Count == 0)
                    return 0;

                try
                {
                    AssociateOrDisassociate(target, resolved.Select(r => r.ToRef()), add);
                    log.Changed += resolved.Count;
                    MarkApplied(existing, resolved, add);
                    return resolved.Count;
                }
                catch (Exception ex2)
                {
                    AddError(ex2.Message);
                    return 0;
                }
            }
        }

        /// <summary>
        /// Folds a successful (dis)association back into the target's known role set, so a later
        /// batch for the same target treats it as settled. Matters for the classic-BU fallback:
        /// two selected BU copies of one logical role resolve onto the same same-BU copy, and
        /// without this the second batch would re-attempt a row the first already applied.
        /// </summary>
        private static void MarkApplied(HashSet<Guid> existing, IEnumerable<RoleItem> roles, bool add)
        {
            foreach (var role in roles)
            {
                if (add)
                    existing.Add(role.Id);
                else
                    existing.Remove(role.Id);
            }
        }

        private void AssociateOrDisassociate(IAssignmentTarget target, IEnumerable<EntityReference> roleRefs, bool add)
        {
            var refs = new EntityReferenceCollection(roleRefs.ToList());
            var relationship = new Relationship(target.RelationshipSchemaName);
            if (add)
                _service.Associate(target.EntityLogicalName, target.Id, relationship, refs);
            else
                _service.Disassociate(target.EntityLogicalName, target.Id, relationship, refs);
        }
    }
}
