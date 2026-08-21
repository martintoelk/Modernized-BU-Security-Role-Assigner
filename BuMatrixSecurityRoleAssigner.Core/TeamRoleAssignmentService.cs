using System;
using System.Collections.Generic;
using System.Linq;
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
        private const string TeamRolesRelationship = Team.Fields.teamroles_association;

        private readonly IOrganizationService _service;

        public TeamRoleAssignmentService(IOrganizationService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public List<TeamItem> RetrieveTeams()
        {
            var query = new QueryExpression(Team.EntityLogicalName)
            {
                ColumnSet = new ColumnSet(Team.Fields.Name, Team.Fields.BusinessUnitId, Team.Fields.TeamType),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
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

            return list;
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

        /// <summary>
        /// Assigns or removes <paramref name="selectedRoles"/> for each of <paramref name="teams"/>.
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
        /// </summary>
        public OperationLog AssignOrRemove(
            IReadOnlyList<TeamItem> teams,
            IReadOnlyList<RoleItem> selectedRoles,
            IReadOnlyList<RoleItem> allRoles,
            bool add,
            bool removeFromAllBus = false,
            Action<string> progress = null)
        {
            if (teams == null) throw new ArgumentNullException(nameof(teams));
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
            var n = 0;

            foreach (var team in teams)
            {
                n++;
                progress?.Invoke($"Processing team {n}/{teams.Count}: {team.Name}");

                HashSet<Guid> existing;
                try
                {
                    existing = GetTeamRoleIds(team.Id);
                }
                catch (Exception ex)
                {
                    log.Errors.Add($"{team.Name}: could not read existing roles ({ex.Message})");
                    continue;
                }

                // Modernized default: assign/remove exactly what the user picked, whatever its BU -
                // unless removeFromAllBus widens each selection to every BU copy of that logical role.
                var targets = new List<RoleItem>();
                var queuedIds = new HashSet<Guid>();
                foreach (var role in selectedRoles)
                {
                    var rows = removeFromAllBus && byRootAll.TryGetValue(role.RootRoleId, out var copies)
                        ? copies
                        : new List<RoleItem> { role };

                    foreach (var row in rows)
                    {
                        // Two selected rows can widen to the same target (e.g. two BU copies of the
                        // same role both selected) - skip dupes so we don't queue it twice.
                        if (!queuedIds.Add(row.Id))
                            continue;

                        if (add)
                        {
                            if (existing.Contains(row.Id))
                                log.AlreadyPresent.Add($"{team.Name} <- {row.Name}");
                            else
                                targets.Add(row);
                        }
                        else
                        {
                            if (!existing.Contains(row.Id))
                                log.NotPresent.Add($"{team.Name} <- {row.Name}");
                            else
                                targets.Add(row);
                        }
                    }
                }

                if (targets.Count == 0)
                    continue;

                try
                {
                    AssociateOrDisassociate(team.Id, targets.Select(r => r.ToRef()), add);
                    log.Changed += targets.Count;
                }
                catch (Exception)
                {
                    // Probe result: either a genuine per-team fault (e.g. Access teams can't hold
                    // security roles) or a classic-BU mismatch. Retry resolving each faulted role
                    // to this team's own BU copy; a successful retry confirms classic-BU behavior.
                    // Roles split three ways:
                    //  - resolved: a distinct BU copy exists and isn't already in the state we
                    //    want (add: not yet assigned; remove: currently assigned) - worth retrying.
                    //  - noBuCopy: no copy of this logical role exists in this team's BU at all -
                    //    a genuine "nothing to fall back to", reported as NoRoleInBu.
                    //  - sameBu: the "closest" copy IS the one we already tried (team is already in
                    //    that role's BU) - the fault isn't a BU mismatch, so it's a real error
                    //    (e.g. Access team), not a BU-copy gap.
                    // The BU-copy's own id (not the originally-selected role's id) is what's checked
                    // against `existing` here - it's the actual row that would be (dis)associated,
                    // and may already be in the wanted state from a prior run (idempotency).
                    var resolved = new List<RoleItem>();
                    var noBuCopy = new List<RoleItem>();
                    var sameBu = new List<RoleItem>();
                    foreach (var role in targets)
                    {
                        if (!byRootBu.TryGetValue(role.RootRoleId, out var buMap) ||
                            !buMap.TryGetValue(team.BusinessUnitId, out var buCopy))
                        {
                            noBuCopy.Add(role);
                            continue;
                        }

                        if (buCopy.Id == role.Id)
                        {
                            sameBu.Add(role);
                            continue;
                        }

                        if (add)
                        {
                            if (existing.Contains(buCopy.Id))
                                log.AlreadyPresent.Add($"{team.Name} <- {buCopy.Name}");
                            else
                                resolved.Add(buCopy);
                        }
                        else
                        {
                            if (!existing.Contains(buCopy.Id))
                                log.NotPresent.Add($"{team.Name} <- {buCopy.Name}");
                            else
                                resolved.Add(buCopy);
                        }
                    }

                    foreach (var role in noBuCopy)
                        log.NoRoleInBu.Add($"{team.Name} ({team.BusinessUnitName}) <- {role.Name}");

                    // sameBu roles shared the failed batch with roles that may genuinely have needed
                    // BU resolution, so the batch fault doesn't necessarily implicate them - retry
                    // them alone to tell a real per-role error apart from collateral batch failure.
                    if (sameBu.Count > 0)
                    {
                        try
                        {
                            AssociateOrDisassociate(team.Id, sameBu.Select(r => r.ToRef()), add);
                            log.Changed += sameBu.Count;
                        }
                        catch (Exception exSameBu)
                        {
                            log.Errors.Add($"{team.Name}: {exSameBu.Message}");
                        }
                    }

                    if (resolved.Count == 0)
                        continue;

                    try
                    {
                        AssociateOrDisassociate(team.Id, resolved.Select(r => r.ToRef()), add);
                        log.Changed += resolved.Count;
                        log.ClassicBuDetected.Add(
                            $"{team.Name} ({team.BusinessUnitName}): matched {resolved.Count} role(s) to the team's own business unit.");
                    }
                    catch (Exception ex2)
                    {
                        log.Errors.Add($"{team.Name}: {ex2.Message}");
                    }
                }
            }

            return log;
        }

        private void AssociateOrDisassociate(Guid teamId, IEnumerable<EntityReference> roleRefs, bool add)
        {
            var refs = new EntityReferenceCollection(roleRefs.ToList());
            if (add)
                _service.Associate(Team.EntityLogicalName, teamId, new Relationship(TeamRolesRelationship), refs);
            else
                _service.Disassociate(Team.EntityLogicalName, teamId, new Relationship(TeamRolesRelationship), refs);
        }
    }
}
