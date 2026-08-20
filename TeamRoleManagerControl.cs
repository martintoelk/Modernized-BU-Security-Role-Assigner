using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using XrmToolBox.Extensibility;

namespace TeamRoleManager
{
    public partial class TeamRoleManagerControl : PluginControlBase
    {
        private const string TeamRolesRelationship = "teamroles_association";

        // Full, unfiltered caches. The list views are populated from these (with text filters applied).
        private List<TeamItem> _allTeams = new List<TeamItem>();
        private List<RoleItem> _allRoles = new List<RoleItem>();

        public TeamRoleManagerControl()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ UI events

        private void tsbLoad_Click(object sender, EventArgs e)
        {
            // ExecuteMethod ensures we have a live connection first (prompts if needed).
            ExecuteMethod(LoadData);
        }

        private void tsbAdd_Click(object sender, EventArgs e) => ExecuteMethod(() => AssignOrRemove(add: true));

        private void tsbRemove_Click(object sender, EventArgs e) => ExecuteMethod(() => AssignOrRemove(add: false));

        private void txtTeamFilter_TextChanged(object sender, EventArgs e) => PopulateTeamList();

        private void txtRoleFilter_TextChanged(object sender, EventArgs e) => PopulateRoleList();

        // ------------------------------------------------------------------ Load

        private void LoadData()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading teams and security roles...",
                Work = (worker, args) =>
                {
                    var teams = RetrieveTeams();
                    var roles = RetrieveRoles();
                    args.Result = Tuple.Create(teams, roles);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var result = (Tuple<List<TeamItem>, List<RoleItem>>)args.Result;
                    _allTeams = result.Item1;
                    _allRoles = result.Item2;
                    PopulateTeamList();
                    PopulateRoleList();
                }
            });
        }

        private List<TeamItem> RetrieveTeams()
        {
            var query = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("teamid", "name", "businessunitid", "teamtype"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.AddOrder("name", OrderType.Ascending);

            var list = new List<TeamItem>();
            EntityCollection ec;
            do
            {
                ec = Service.RetrieveMultiple(query);
                foreach (var t in ec.Entities)
                {
                    var bu = t.GetAttributeValue<EntityReference>("businessunitid");
                    list.Add(new TeamItem
                    {
                        Id = t.Id,
                        Name = t.GetAttributeValue<string>("name"),
                        BusinessUnitId = bu?.Id ?? Guid.Empty,
                        BusinessUnitName = bu?.Name ?? string.Empty,
                        TeamType = t.FormattedValues.ContainsKey("teamtype")
                            ? t.FormattedValues["teamtype"]
                            : string.Empty
                    });
                }
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = ec.PagingCookie;
            }
            while (ec.MoreRecords);

            return list;
        }

        private List<RoleItem> RetrieveRoles()
        {
            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("roleid", "name", "businessunitid", "parentrootroleid"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            query.AddOrder("name", OrderType.Ascending);

            var list = new List<RoleItem>();
            EntityCollection ec;
            do
            {
                ec = Service.RetrieveMultiple(query);
                foreach (var r in ec.Entities)
                {
                    var bu = r.GetAttributeValue<EntityReference>("businessunitid");
                    var root = r.GetAttributeValue<EntityReference>("parentrootroleid");
                    list.Add(new RoleItem
                    {
                        Id = r.Id,
                        Name = r.GetAttributeValue<string>("name"),
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

        // ------------------------------------------------------------------ List population + filtering

        private static bool Match(string filter, params string[] fields)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            return fields.Any(f => !string.IsNullOrEmpty(f) &&
                                   f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void PopulateTeamList()
        {
            var filter = txtTeamFilter.Text?.Trim();
            lvTeams.BeginUpdate();
            lvTeams.Items.Clear();
            foreach (var t in _allTeams.Where(t => Match(filter, t.Name, t.BusinessUnitName)))
            {
                var item = new ListViewItem(t.Name);
                item.SubItems.Add(t.BusinessUnitName);
                item.SubItems.Add(t.TeamType);
                item.Tag = t;
                lvTeams.Items.Add(item);
            }
            lvTeams.EndUpdate();
            UpdateStatus();
        }

        private void PopulateRoleList()
        {
            var filter = txtRoleFilter.Text?.Trim();
            lvRoles.BeginUpdate();
            lvRoles.Items.Clear();
            foreach (var r in _allRoles.Where(r => Match(filter, r.Name, r.BusinessUnitName)))
            {
                var item = new ListViewItem(r.Name);
                item.SubItems.Add(r.BusinessUnitName);
                item.Tag = r;
                lvRoles.Items.Add(item);
            }
            lvRoles.EndUpdate();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            lblStatus.Text = $"Teams: {lvTeams.Items.Count} shown ({_allTeams.Count} total)   |   " +
                             $"Roles: {lvRoles.Items.Count} shown ({_allRoles.Count} total)";
        }

        private List<TeamItem> GetSelectedTeams() =>
            lvTeams.SelectedItems.Cast<ListViewItem>().Select(i => (TeamItem)i.Tag).ToList();

        private List<RoleItem> GetSelectedRoles() =>
            lvRoles.SelectedItems.Cast<ListViewItem>().Select(i => (RoleItem)i.Tag).ToList();

        // ------------------------------------------------------------------ Add / Remove

        private void AssignOrRemove(bool add)
        {
            var teams = GetSelectedTeams();
            var roles = GetSelectedRoles();

            if (teams.Count == 0 || roles.Count == 0)
            {
                MessageBox.Show(this, "Select at least one team and one role.", "Nothing to do",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Default (modernized business units): assign the EXACT role selected, keeping its BU.
            // Opt-in (classic model): resolve each role to the copy in the team's own BU.
            var matchBu = tsbMatchBu.Checked;

            var confirm = MessageBox.Show(this,
                $"{(add ? "Assign" : "Remove")} {roles.Count} role(s) {(add ? "to" : "from")} {teams.Count} team(s)?" +
                (matchBu ? "\n\nClassic mode: each role will be matched to the team's own business unit." : ""),
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            // Only needed in classic mode: index every role copy by (root role, business unit).
            // root -> (buId -> role)
            var byRootBu = matchBu
                ? _allRoles.GroupBy(r => r.RootRoleId)
                           .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.BusinessUnitId, r => r))
                : null;

            WorkAsync(new WorkAsyncInfo
            {
                Message = add ? "Assigning roles..." : "Removing roles...",
                Work = (worker, args) =>
                {
                    var log = new OperationLog();
                    var n = 0;

                    foreach (var team in teams)
                    {
                        n++;
                        SetWorkingMessage($"Processing team {n}/{teams.Count}: {team.Name}");

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

                        foreach (var role in roles)
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
                                Service.Associate("team", team.Id, new Relationship(TeamRolesRelationship),
                                    new EntityReferenceCollection(toAssign));
                                log.Changed += toAssign.Count;
                            }
                            else if (!add && toRemove.Count > 0)
                            {
                                Service.Disassociate("team", team.Id, new Relationship(TeamRolesRelationship),
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

                    args.Result = log;
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Operation failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var log = (OperationLog)args.Result;
                    var icon = log.Errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
                    MessageBox.Show(this, log.Summary(add), "Done", MessageBoxButtons.OK, icon);
                }
            });
        }

        /// <summary>Role ids (BU-specific) currently associated with the given team.</summary>
        private HashSet<Guid> GetTeamRoleIds(Guid teamId)
        {
            var query = new QueryExpression("role")
            {
                ColumnSet = new ColumnSet("roleid")
            };
            var link = query.AddLink("teamroles", "roleid", "roleid");
            link.LinkCriteria.AddCondition("teamid", ConditionOperator.Equal, teamId);

            var result = Service.RetrieveMultiple(query);
            return new HashSet<Guid>(result.Entities.Select(e => e.Id));
        }
    }
}
