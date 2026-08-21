using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BuMatrixSecurityRoleAssigner.Core;
using XrmToolBox.Extensibility;

namespace BuMatrixSecurityRoleAssigner
{
    public partial class BuMatrixSecurityRoleAssignerControl : PluginControlBase
    {
        // Full, unfiltered caches. The list views are populated from these (with text filters applied).
        private List<TeamItem> _allTeams = new List<TeamItem>();
        private List<RoleItem> _allRoles = new List<RoleItem>();

        public BuMatrixSecurityRoleAssignerControl()
        {
            InitializeComponent();
        }

        // ------------------------------------------------------------------ UI events

        private void tsbLoad_Click(object sender, EventArgs e)
        {
            // ExecuteMethod ensures we have a live connection first (prompts if needed).
            ExecuteMethod(LoadData);
        }

        private void tsbAdd_Click(object sender, EventArgs e)
        {
            if (!RequireConnection()) return;
            ExecuteMethod(() => AssignOrRemove(add: true));
        }

        private void tsbRemove_Click(object sender, EventArgs e)
        {
            if (!RequireConnection()) return;
            ExecuteMethod(() => AssignOrRemove(add: false));
        }

        // Only "Load / Refresh" should prompt the connect dialog. Add/Remove need an existing
        // connection (and loaded data) and just tell the user to use Load instead of connecting.
        private bool RequireConnection()
        {
            if (Service != null) return true;
            MessageBox.Show(this, "Connect to an environment and click \"Load / Refresh\" first.",
                "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

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
                    var service = new TeamRoleAssignmentService(Service);
                    var teams = service.RetrieveTeams();
                    var roles = service.RetrieveRoles();
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

            // Classic-BU teams are auto-detected via a behavioral probe in the service, not a
            // manual toggle; a successful fallback is surfaced afterwards via log.ClassicBuDetected.
            var removeFromAllBus = !add && tsbRemoveAllBus.Checked;
            var warning = removeFromAllBus
                ? "\n\nWARNING: \"Remove from all BUs\" is on - every business-unit copy of each " +
                  "selected role currently assigned to the selected team(s) will be removed, not just " +
                  "the row(s) you selected."
                : "";

            var confirm = MessageBox.Show(this,
                $"{(add ? "Assign" : "Remove")} {roles.Count} role(s) {(add ? "to" : "from")} {teams.Count} team(s)?" + warning,
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            WorkAsync(new WorkAsyncInfo
            {
                Message = add ? "Assigning roles..." : "Removing roles...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleAssignmentService(Service);
                    args.Result = service.AssignOrRemove(
                        teams, roles, _allRoles, add, removeFromAllBus,
                        progress: message => SetWorkingMessage(message));
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
                    var icon = log.Errors.Count > 0 || log.ClassicBuDetected.Count > 0
                        ? MessageBoxIcon.Warning
                        : MessageBoxIcon.Information;
                    MessageBox.Show(this, log.Summary(add), "Done", MessageBoxButtons.OK, icon);
                }
            });
        }
    }
}
