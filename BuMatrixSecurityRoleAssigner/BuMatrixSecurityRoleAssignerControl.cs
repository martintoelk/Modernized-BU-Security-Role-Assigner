using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using BuMatrixSecurityRoleAssigner.Core;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;

namespace BuMatrixSecurityRoleAssigner
{
    // IMessageBusHost is XrmToolBox's host-mediated channel between tools; implementing it is
    // what lets this tool hand a selected team/user to the Role Inspector (issue #17). See
    // OpenInInspector for what the host does with the message.
    public partial class BuMatrixSecurityRoleAssignerControl : PluginControlBase, IMessageBusHost
    {
        // Full, unfiltered caches. The list views are populated from these (with text filters applied).
        private List<TeamItem> _allTeams = new List<TeamItem>();
        private List<UserItem> _allUsers = new List<UserItem>();
        private List<RoleItem> _allRoles = new List<RoleItem>();

        // Informational per GetModernizedBuStatus's doc comment, but also drives whether the roles
        // list collapses BU-duplicate rows (see PopulateRoleList) - classic-BU orgs otherwise show a
        // confusing one-row-per-BU-copy list for what is really one logical role.
        private ModernizedBuStatus _modernizedBuStatus = ModernizedBuStatus.Unknown;

        // Click-to-sort state, one per grid (issue #23). Held here rather than in the ListViews
        // so it survives repopulation - the lists are rebuilt from the caches on every filter
        // keystroke and on the Teams/Users toggle.
        private readonly GridSort _targetSort = new GridSort();
        private readonly GridSort _roleSort = new GridSort();

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

        private void tsbInspect_Click(object sender, EventArgs e) => OpenInInspector();

        private void lvTeams_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            _targetSort.HeaderClicked(e.Column);
            PopulateTargetList();
        }

        private void lvRoles_ColumnClick(object sender, ColumnClickEventArgs e)
        {
            _roleSort.HeaderClicked(e.Column);
            PopulateRoleList();
        }

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
                    var modernizedBuStatus = service.GetModernizedBuStatus();
                    args.Result = Tuple.Create(teams, users, roles, modernizedBuStatus);
                },
                PostWorkCallBack = args =>
                {
                    if (args.Error != null)
                    {
                        MessageBox.Show(this, args.Error.Message, "Load failed",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    var result = (Tuple<List<TeamItem>, List<UserItem>, List<RoleItem>, ModernizedBuStatus>)args.Result;
                    _allTeams = result.Item1;
                    _allUsers = result.Item2;
                    _allRoles = result.Item3;
                    _modernizedBuStatus = result.Item4;
                    UpdateBuModeIndicator();
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

            IEnumerable<ListViewItem> rows;
            if (UsersMode)
            {
                rows = _allUsers
                    .Where(u => Match(filter, u.Name, u.BusinessUnitName))
                    .Select(u => Row(u, u.Name, u.BusinessUnitName, u.IsDisabled ? "Yes" : ""));
            }
            else
            {
                rows = _allTeams
                    .Where(t => Match(filter, t.Name, t.BusinessUnitName))
                    .Select(t => Row(t, t.Name, t.BusinessUnitName, t.TeamType));
            }

            Fill(lvTeams, rows, _targetSort);
            UpdateStatus();
        }

        /// <summary>A grid row: the cell texts as shown, with the underlying model on the Tag.</summary>
        private static ListViewItem Row(object model, params string[] cells)
        {
            var item = new ListViewItem(cells[0]) { Tag = model };
            for (var i = 1; i < cells.Length; i++)
                item.SubItems.Add(cells[i]);
            return item;
        }

        // Sorting the built rows (rather than the model lists) keeps "what you sort" identical to
        // "what you see" - GridSort only ever reads cell text - so one code path sorts both grids
        // regardless of what their rows are made of.
        private static void Fill(ListView lv, IEnumerable<ListViewItem> rows, GridSort sort)
        {
            // Rows are rebuilt from scratch on every repopulation, so carry the selection over by
            // model identity - sorting a grid you have already made a selection in must not
            // silently empty it. Tags are instances from the _all* caches, which only change on a
            // reload, so reference equality is the right identity here.
            var selected = new HashSet<object>(lv.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag));

            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Items.AddRange(sort.Apply(rows, (r, column) => r.SubItems[column].Text).ToArray());

            // Only the rows that were selected are touched: fresh items come in unselected, and
            // each assignment is a separate native item-state call - on a large org that would
            // otherwise cost thousands of them per filter keystroke.
            ListViewItem anchor = null;
            if (selected.Count > 0)
            {
                foreach (ListViewItem item in lv.Items)
                {
                    if (!selected.Contains(item.Tag)) continue;
                    item.Selected = true;
                    if (anchor == null) anchor = item;
                }
            }

            for (var i = 0; i < lv.Columns.Count; i++)
                lv.Columns[i].Text = sort.DecorateHeader(lv.Columns[i].Text, i);
            lv.EndUpdate();

            // A restored selection with no focused item leaves the shift-click anchor at index 0,
            // so the next shift-click would extend from the top of the list instead of from what
            // the user has selected - silently widening the batch. Scrolling to it also keeps the
            // selection in view when the sort moved it far down. Done after EndUpdate, since
            // EnsureVisible can't scroll a list whose painting is still suspended.
            if (anchor != null)
            {
                anchor.Focused = true;
                anchor.EnsureVisible();
            }
        }

        // Drives the read-only mode indicator above the roles grid (mirrors the Teams/Users
        // toggle above the other grid) - never a user setting, just a readout of the probe result.
        private void UpdateBuModeIndicator()
        {
            lblBuModeIndicator.Image?.Dispose();
            switch (_modernizedBuStatus)
            {
                case ModernizedBuStatus.Yes:
                    lblBuModeIndicator.Text = "Mode: Modernized BU";
                    lblBuModeIndicator.Image = CreateModernizedBuIcon();
                    break;
                case ModernizedBuStatus.No:
                    lblBuModeIndicator.Text = "Mode: Classic BU";
                    lblBuModeIndicator.Image = CreateClassicBuIcon();
                    break;
                default:
                    lblBuModeIndicator.Text = "Mode: Unknown";
                    lblBuModeIndicator.Image = CreateUnknownBuIcon();
                    break;
            }
        }

        // Classic-BU orgs (status No) typically carry a redundant copy of every role in every BU;
        // showing one row per copy is noisy and the per-row BU doesn't mean anything the way it
        // does for modernized BUs. Collapse to one row per logical role (RootRoleId) instead, and
        // hide the BU column. Yes/Unknown keep today's per-BU-row display unchanged (fail open -
        // never collapse on a failed probe).
        private bool CollapseRolesByRootRole => _modernizedBuStatus == ModernizedBuStatus.No;

        // Tracks the collapse state the BU column width was last set for, so PopulateRoleList only
        // touches the width on an actual collapse/expand transition - not on every repopulation
        // (e.g. each filter keystroke), which would otherwise clobber a manual column resize.
        private bool? _buColumnWidthSetForCollapse;

        private void PopulateRoleList()
        {
            var filter = txtRoleFilter.Text?.Trim();
            var collapse = CollapseRolesByRootRole;
            if (_buColumnWidthSetForCollapse != collapse)
            {
                lvRoles.Columns[1].Width = collapse ? 0 : 200;
                _buColumnWidthSetForCollapse = collapse;
            }

            IEnumerable<RoleItem> roles = collapse
                ? _allRoles.GroupBy(r => r.RootRoleId)
                           .Select(g => g.FirstOrDefault(r => r.Id == g.Key) ?? g.First())
                           .OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                : _allRoles;

            Fill(lvRoles,
                 roles.Where(r => Match(filter, r.Name, collapse ? null : r.BusinessUnitName))
                      .Select(r => Row(r, r.Name, collapse ? "" : r.BusinessUnitName)),
                 _roleSort);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var targetLabel = UsersMode ? "Users" : "Teams";
            var targetTotal = UsersMode ? _allUsers.Count : _allTeams.Count;
            var roleTotal = CollapseRolesByRootRole
                ? _allRoles.Select(r => r.RootRoleId).Distinct().Count()
                : _allRoles.Count;
            lblStatus.Text = $"{targetLabel}: {lvTeams.Items.Count} shown ({targetTotal} total)   |   " +
                             $"Roles: {lvRoles.Items.Count} shown ({roleTotal} total)";
        }

        private List<IAssignmentTarget> GetSelectedTargets() =>
            lvTeams.SelectedItems.Cast<ListViewItem>().Select(i => (IAssignmentTarget)i.Tag).ToList();

        private List<RoleItem> GetSelectedRoles() =>
            lvRoles.SelectedItems.Cast<ListViewItem>().Select(i => (RoleItem)i.Tag).ToList();

        // ------------------------------------------------------------------ Role Inspector handoff

        /// <summary>
        /// Display name of the tool we hand off to, matched by XrmToolBox against that tool's MEF
        /// <c>ExportMetadata("Name", ...)</c>. String-matched, not type-matched - the two plugins
        /// never reference each other - so renaming the Inspector's export breaks this silently,
        /// with no compiler error.
        /// </summary>
        private const string InspectorPluginName = "User/Team Role Inspector";

        /// <summary>
        /// Raised to ask the host to route a message to another tool. XrmToolBox subscribes to
        /// this on every loaded plugin control and does the routing itself; nothing here talks to
        /// the other tool directly.
        /// </summary>
        public event EventHandler<MessageBusEventArgs> OnOutgoingMessage;

        /// <summary>
        /// Required by <see cref="IMessageBusHost"/>. This tool only ever sends - nothing targets
        /// it today - and a receiver that cannot act on a message should ignore it rather than
        /// fail in front of the user, so this is deliberately a no-op. The reverse direction
        /// ("assign roles to what I am inspecting") needs a sender in the Inspector first.
        /// </summary>
        public void OnIncomingMessage(MessageBusEventArgs message)
        {
        }

        /// <summary>
        /// Hands the selected team/user to the Role Inspector. The host resolves
        /// <see cref="InspectorPluginName"/> against the installed tools, opens it if it isn't
        /// already open on this connection, brings it to the front, and delivers the payload -
        /// and shows its own error if the tool isn't installed, which is why there is no
        /// "is it installed" check here to get out of date.
        /// </summary>
        private void OpenInInspector()
        {
            var targets = GetSelectedTargets();
            var targetNoun = UsersMode ? "user" : "team";

            if (targets.Count == 0)
            {
                MessageBox.Show(this, $"Select the {targetNoun} you want to inspect.",
                    "Nothing to inspect", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Unlike Add/Remove, this is not a batch operation: the Inspector shows one record at
            // a time, so silently picking one out of a multi-selection would open something the
            // user didn't ask for.
            if (targets.Count > 1)
            {
                MessageBox.Show(this,
                    $"The Role Inspector shows one {targetNoun} at a time. Select a single {targetNoun} to inspect.",
                    "Select one row", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // The host subscribes to this on every plugin control it loads, so a null here means
            // we aren't running under a host that routes messages. Worth saying out loud: every
            // other outcome on this path reports itself (the host shows its own "Cannot switch to
            // tool ..." when the target isn't installed), and a button that does nothing at all,
            // silently, is indistinguishable from a hang.
            if (OnOutgoingMessage == null)
            {
                MessageBox.Show(this,
                    "This copy of XrmToolBox did not connect the tool-to-tool message bus, so the " +
                    "Role Inspector can't be opened from here.\r\n\r\n" +
                    "Open it from the tool list instead.",
                    "Can't open the Role Inspector", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Sent as a string rather than a RoleHandoff instance: the Inspector is a separate
            // assembly that cannot name this type. See RoleHandoff's remarks.
            OnOutgoingMessage(this, new MessageBusEventArgs(InspectorPluginName)
            {
                TargetArgument = RoleHandoff.ForTarget(targets[0]).ToPayload()
            });
        }

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
                    var stopwatch = Stopwatch.StartNew();
                    var service = new TeamRoleAssignmentService(Service);
                    var targetNoun = UsersMode ? "User" : "Team";
                    args.Result = service.AssignOrRemove(
                        targets, roles, _allRoles, add, removeFromAllBus,
                        progress: p => SetWorkingMessage(FormatProgressMessage(p, add, stopwatch.Elapsed, targetNoun)));
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

        // Turns the role-unit progress the service reports into a message with overall progress
        // and an ETA, so large batches show more than just a spinner. Units are (target x role)
        // pairs rather than targets: with 3 teams x 40 roles a target-denominated bar would only
        // ever read 0/33/66% and show no ETA at all until the first team finished. ETA is derived
        // from throughput-so-far (elapsed / unitsDone) rather than a fixed estimate, since
        // per-unit work (retries, skips) varies. No estimate is shown until at least one unit has
        // settled, since a rate from zero completions is meaningless.
        private static string FormatProgressMessage(AssignRemoveProgress p, bool add, TimeSpan elapsed, string targetNoun)
        {
            var verb = add ? "Assigning" : "Removing";
            var prep = add ? "to" : "from";
            var percent = p.TotalUnits > 0 ? p.UnitsDone * 100 / p.TotalUnits : 0;
            // Roles per target, taken from the unit total rather than the raw selection count, so
            // both halves of the message agree: with "Remove from all BUs" on, one selected row
            // widens to every BU copy, and "1 Role ... 0 of 15 role assignments" would just look
            // wrong.
            var roleCount = p.TotalTargets > 0 ? p.TotalUnits / p.TotalTargets : 0;
            var targetNounLower = targetNoun.ToLowerInvariant();
            // The closing report arrives with every target counted, so clamp rather than showing
            // "team 4 of 3".
            var currentTargetNumber = Math.Min(p.TargetsDone + 1, p.TotalTargets);
            var header = $"{verb} {roleCount} {Pluralize("Role", roleCount)} {prep} {p.TotalTargets} {Pluralize(targetNoun, p.TotalTargets)}. " +
                         $"Current progress: {p.UnitsDone} of {p.TotalUnits} role {Pluralize("assignment", p.TotalUnits)} ({percent}%), " +
                         $"{targetNounLower} {currentTargetNumber} of {p.TotalTargets}";

            if (p.UnitsDone == 0)
                return header;

            var remaining = p.TotalUnits - p.UnitsDone;
            var estimate = TimeSpan.FromTicks(elapsed.Ticks / p.UnitsDone * remaining);
            return $"{header} ETA: {FormatEta(estimate)}";
        }

        private static string Pluralize(string noun, int count) => count == 1 ? noun : noun + "s";

        private static string FormatEta(TimeSpan eta) =>
            $"{(int)eta.TotalHours:00}:{eta.Minutes:00}:{eta.Seconds:00}";
    }
}
