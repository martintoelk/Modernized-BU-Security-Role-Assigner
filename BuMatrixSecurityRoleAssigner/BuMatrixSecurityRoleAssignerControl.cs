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
        private List<UserItem> _allUsers = new List<UserItem>();
        private List<RoleItem> _allRoles = new List<RoleItem>();

        // Teams vs Users - never mixed. tsbUsersMode.Checked is the single source of truth; this
        // just makes call sites read as "which mode", not "which checkbox".
        private bool UsersMode => tsbUsersMode.Checked;

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

        // ExecuteMethod itself prompts the XTB connection dialog when Service is null, so Add/
        // Remove don't need a manual "connect first" check - that would just replace the host's
        // own connection UI with a plain message box. If nothing's been loaded yet, the existing
        // "select at least one..." check in AssignOrRemove covers the empty-list case gracefully.
        private void btnAdd_Click(object sender, EventArgs e) =>
            ExecuteMethod(() => AssignOrRemove(add: true));

        private void btnRemove_Click(object sender, EventArgs e) =>
            ExecuteMethod(() => AssignOrRemove(add: false));

        private void txtTeamFilter_TextChanged(object sender, EventArgs e) => PopulateTargetList();

        private void tsbUsersMode_CheckedChanged(object sender, EventArgs e)
        {
            tsbUsersMode.Text = UsersMode ? "Mode: Users" : "Mode: Teams";
            tsbUsersMode.Image = UsersMode ? CreateUserIcon() : CreateTeamsIcon();
            lblTeams.Text = UsersMode ? "Users (multi-select)" : "Teams (multi-select)";

            lvTeams.Columns[0].Text = UsersMode ? "User" : "Team";
            lvTeams.Columns[2].Text = UsersMode ? "Disabled" : "Type";

            PopulateTargetList();
        }

        private void txtRoleFilter_TextChanged(object sender, EventArgs e) => PopulateRoleList();

        // ------------------------------------------------------------------ Load

        private void LoadData()
        {
            WorkAsync(new WorkAsyncInfo
            {
                Message = "Loading teams, users and security roles...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleAssignmentService(Service);
                    var teams = service.RetrieveTeams();
                    var users = service.RetrieveUsers();
                    var roles = service.RetrieveRoles();
                    args.Result = Tuple.Create(teams, users, roles);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var result = (Tuple<List<TeamItem>, List<UserItem>, List<RoleItem>>)args.Result;
                    _allTeams = result.Item1;
                    _allUsers = result.Item2;
                    _allRoles = result.Item3;
                    PopulateTargetList();
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

        private void PopulateTargetList()
        {
            var filter = txtTeamFilter.Text?.Trim();
            lvTeams.BeginUpdate();
            lvTeams.Items.Clear();

            if (UsersMode)
            {
                foreach (var u in _allUsers.Where(u => Match(filter, u.Name, u.BusinessUnitName)))
                {
                    var item = new ListViewItem(u.Name);
                    item.SubItems.Add(u.BusinessUnitName);
                    item.SubItems.Add(u.IsDisabled ? "Yes" : "");
                    item.Tag = u;
                    lvTeams.Items.Add(item);
                }
            }
            else
            {
                foreach (var t in _allTeams.Where(t => Match(filter, t.Name, t.BusinessUnitName)))
                {
                    var item = new ListViewItem(t.Name);
                    item.SubItems.Add(t.BusinessUnitName);
                    item.SubItems.Add(t.TeamType);
                    item.Tag = t;
                    lvTeams.Items.Add(item);
                }
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
            var targetLabel = UsersMode ? "Users" : "Teams";
            var targetTotal = UsersMode ? _allUsers.Count : _allTeams.Count;
            lblStatus.Text = $"{targetLabel}: {lvTeams.Items.Count} shown ({targetTotal} total)   |   " +
                             $"Roles: {lvRoles.Items.Count} shown ({_allRoles.Count} total)";
        }

        private List<IAssignmentTarget> GetSelectedTargets() =>
            lvTeams.SelectedItems.Cast<ListViewItem>().Select(i => (IAssignmentTarget)i.Tag).ToList();

        private List<RoleItem> GetSelectedRoles() =>
            lvRoles.SelectedItems.Cast<ListViewItem>().Select(i => (RoleItem)i.Tag).ToList();

        // ------------------------------------------------------------------ Add / Remove

        private void AssignOrRemove(bool add)
        {
            var targets = GetSelectedTargets();
            var roles = GetSelectedRoles();
            var targetLabel = UsersMode ? "user(s)" : "team(s)";

            if (targets.Count == 0 || roles.Count == 0)
            {
                MessageBox.Show(this, $"Select at least one {(UsersMode ? "user" : "team")} and one role.",
                    "Nothing to do", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Classic-BU targets are auto-detected via a behavioral probe in the service, not a
            // manual toggle; a successful fallback is surfaced afterwards via log.ClassicBuDetected.
            var removeFromAllBus = !add && chkRemoveAllBus.Checked;
            var warning = removeFromAllBus
                ? $"\n\nWARNING: \"Remove from all BUs\" is on - every business-unit copy of each " +
                  $"selected role currently assigned to the selected {targetLabel} will be removed, not " +
                  "just the row(s) you selected."
                : "";

            var confirm = MessageBox.Show(this,
                $"{(add ? "Assign" : "Remove")} {roles.Count} role(s) {(add ? "to" : "from")} {targets.Count} {targetLabel}?" + warning,
                "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            WorkAsync(new WorkAsyncInfo
            {
                Message = add ? "Assigning roles..." : "Removing roles...",
                Work = (worker, args) =>
                {
                    var service = new TeamRoleAssignmentService(Service);
                    args.Result = service.AssignOrRemove(
                        targets, roles, _allRoles, add, removeFromAllBus,
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
