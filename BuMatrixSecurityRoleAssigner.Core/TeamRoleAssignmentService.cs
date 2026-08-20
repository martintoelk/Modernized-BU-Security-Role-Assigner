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
        /// When <paramref name="matchBu"/> is true (classic model), each role is first resolved to
        /// the copy that lives in the team's own business unit, using <paramref name="allRoles"/>
        /// (every role, not just the selection) to build the root-role -> BU -> role index.
        /// </summary>
        public OperationLog AssignOrRemove(
            IReadOnlyList<TeamItem> teams,
            IReadOnlyList<RoleItem> selectedRoles,
            IReadOnlyList<RoleItem> allRoles,
            bool add,
            bool matchBu,
            Action<string> progress = null)
        {
            if (teams == null) throw new ArgumentNullException(nameof(teams));
            if (selectedRoles == null) throw new ArgumentNullException(nameof(selectedRoles));
            if (allRoles == null) throw new ArgumentNullException(nameof(allRoles));

            // Only needed in classic mode: index every role copy by (root role, business unit).
            // root -> (buId -> role)
            var byRootBu = matchBu
                ? allRoles.GroupBy(r => r.RootRoleId)
                          .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.BusinessUnitId, r => r))
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

                var toAssign = new List<EntityReference>();
                var toRemove = new List<EntityReference>();

                foreach (var role in selectedRoles)
                {
                    RoleItem targetRole;
                    if (matchBu)
                    {
                        // Classic model: use the copy of this role that lives in THIS team's BU.
                        if (!byRootBu.TryGetValue(role.RootRoleId, out var buMap) ||
                            !buMap.TryGetValue(team.BusinessUnitId, out targetRole))
                        {
                            log.NoRoleInBu.Add($"{team.Name} ({team.BusinessUnitName}) <- {role.Name}");
                            continue;
                        }
                    }
                    else
                    {
                        // Modernized BUs: assign exactly what the user picked, whatever its BU.
                        targetRole = role;
                    }

                    if (add)
                    {
                        if (existing.Contains(targetRole.Id))
                            log.AlreadyPresent.Add($"{team.Name} <- {targetRole.Name}");
                        else
                            toAssign.Add(targetRole.ToRef());
                    }
                    else
                    {
                        if (!existing.Contains(targetRole.Id))
                            log.NotPresent.Add($"{team.Name} <- {targetRole.Name}");
                        else
                            toRemove.Add(targetRole.ToRef());
                    }
                }

                try
                {
                    if (add && toAssign.Count > 0)
                    {
                        _service.Associate(Team.EntityLogicalName, team.Id, new Relationship(TeamRolesRelationship),
                            new EntityReferenceCollection(toAssign));
                        log.Changed += toAssign.Count;
                    }
                    else if (!add && toRemove.Count > 0)
                    {
                        _service.Disassociate(Team.EntityLogicalName, team.Id, new Relationship(TeamRolesRelationship),
                            new EntityReferenceCollection(toRemove));
                        log.Changed += toRemove.Count;
                    }
                }
                catch (Exception ex)
                {
                    // e.g. Access teams (teamtype = Access) cannot hold security roles -> surfaced here.
                    log.Errors.Add($"{team.Name}: {ex.Message}");
                }
            }

            return log;
        }
    }
}
