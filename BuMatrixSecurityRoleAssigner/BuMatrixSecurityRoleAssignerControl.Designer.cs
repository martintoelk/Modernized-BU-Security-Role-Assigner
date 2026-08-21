using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BuMatrixSecurityRoleAssigner
{
    partial class BuMatrixSecurityRoleAssignerControl
    {
        private ToolStrip toolStrip;
        private ToolStripButton tsbLoad;
        private ToolStripButton tsbAdd;
        private ToolStripButton tsbRemove;
        private ToolStripButton tsbRemoveAllBus;
        private ToolStripButton tsbUsersMode;

        private SplitContainer split;

        private Label lblTeams;
        private TextBox txtTeamFilter;
        private ListView lvTeams;

        private Label lblRoles;
        private TextBox txtRoleFilter;
        private ListView lvRoles;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;

        private void InitializeComponent()
        {
            this.toolStrip = new ToolStrip();
            this.tsbLoad = new ToolStripButton();
            this.tsbAdd = new ToolStripButton();
            this.tsbRemove = new ToolStripButton();
            this.tsbRemoveAllBus = new ToolStripButton();
            this.tsbUsersMode = new ToolStripButton();

            this.split = new SplitContainer();

            this.lblTeams = new Label();
            this.txtTeamFilter = new TextBox();
            this.lvTeams = new ListView();

            this.lblRoles = new Label();
            this.txtRoleFilter = new TextBox();
            this.lvRoles = new ListView();

            this.statusStrip = new StatusStrip();
            this.lblStatus = new ToolStripStatusLabel();

            this.SuspendLayout();

            // ---- ToolStrip ----
            this.tsbLoad.Text = "Load / Refresh";
            this.tsbLoad.DisplayStyle = ToolStripItemDisplayStyle.Text;
            this.tsbLoad.Click += new System.EventHandler(this.tsbLoad_Click);

            // Mode toggle: off = Teams (default), on = Users. Switching swaps the target list's
            // contents and columns - the two modes are never mixed in one Add/Remove call.
            this.tsbUsersMode.Text = "Mode: Teams";
            this.tsbUsersMode.DisplayStyle = ToolStripItemDisplayStyle.Text;
            this.tsbUsersMode.CheckOnClick = true;
            this.tsbUsersMode.Checked = false;
            this.tsbUsersMode.ToolTipText = "Toggle between assigning roles to Teams or to Users.";
            this.tsbUsersMode.CheckedChanged += new System.EventHandler(this.tsbUsersMode_CheckedChanged);

            this.tsbAdd.Text = "Add roles to team(s)";
            this.tsbAdd.Image = CreateGlyphIcon("+", Color.FromArgb(0, 130, 0));
            this.tsbAdd.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            this.tsbAdd.Alignment = ToolStripItemAlignment.Right;
            this.tsbAdd.Click += new System.EventHandler(this.tsbAdd_Click);

            this.tsbRemove.Text = "Remove roles from team(s)";
            this.tsbRemove.Image = CreateGlyphIcon("-", Color.FromArgb(170, 0, 0));
            this.tsbRemove.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            this.tsbRemove.Alignment = ToolStripItemAlignment.Right;
            this.tsbRemove.Click += new System.EventHandler(this.tsbRemove_Click);

            // Classic-BU handling is now auto-detected (behavioral probe in TeamRoleAssignmentService)
            // rather than a manual toggle - see AssignOrRemove and OperationLog.ClassicBuDetected.

            // Remove-only opt-in toggle. Off (default): remove only the exact role row(s) selected,
            // i.e. just that business-unit copy. On: for each selected role, remove EVERY
            // business-unit copy of that role currently assigned to the selected team(s).
            this.tsbRemoveAllBus.Text = "Remove from all BUs";
            this.tsbRemoveAllBus.DisplayStyle = ToolStripItemDisplayStyle.Text;
            this.tsbRemoveAllBus.CheckOnClick = true;
            this.tsbRemoveAllBus.Checked = false;
            this.tsbRemoveAllBus.ToolTipText =
                "Remove only (ignored when adding). Off (default): remove only the exact role row(s) " +
                "you selected, i.e. just that business-unit copy.\r\n" +
                "On: for each selected role, remove EVERY business-unit copy of that role currently " +
                "assigned to the selected team(s).";

            // Add/Remove are right-aligned in add order (Add, then Remove) so they render
            // left-to-right as "Add roles to team(s)  Remove roles from team(s)" on the right edge.
            // tsbRemoveAllBus sits left of them, on the left edge with tsbLoad.
            this.toolStrip.Items.AddRange(new ToolStripItem[]
            {
                this.tsbLoad, this.tsbUsersMode, this.tsbRemoveAllBus, this.tsbAdd, this.tsbRemove
            });
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.GripStyle = ToolStripGripStyle.Hidden;

            // ---- SplitContainer (teams | roles) ----
            this.split.Dock = DockStyle.Fill;
            this.split.Orientation = Orientation.Vertical;
            this.split.SplitterWidth = 6;

            // Left: Roles
            this.lblRoles.Text = "Security roles (multi-select) - Business Unit shown";
            this.lblRoles.Dock = DockStyle.Top;
            this.lblRoles.Height = 20;
            this.lblRoles.Padding = new Padding(2, 3, 0, 0);

            this.txtRoleFilter.Dock = DockStyle.Top;
            this.txtRoleFilter.TextChanged += new System.EventHandler(this.txtRoleFilter_TextChanged);

            this.lvRoles.Dock = DockStyle.Fill;
            this.lvRoles.View = View.Details;
            this.lvRoles.FullRowSelect = true;
            this.lvRoles.MultiSelect = true;
            this.lvRoles.HideSelection = false;
            this.lvRoles.Columns.Add("Security Role", 240);
            this.lvRoles.Columns.Add("Business Unit", 200);

            // NOTE: add order = ListView draws top-most last, so add Fill first, then the Top items.
            this.split.Panel1.Controls.Add(this.lvRoles);
            this.split.Panel1.Controls.Add(this.txtRoleFilter);
            this.split.Panel1.Controls.Add(this.lblRoles);

            // Right: Teams
            this.lblTeams.Text = "Teams (multi-select)";
            this.lblTeams.Dock = DockStyle.Top;
            this.lblTeams.Height = 20;
            this.lblTeams.Padding = new Padding(2, 3, 0, 0);

            this.txtTeamFilter.Dock = DockStyle.Top;
            this.txtTeamFilter.TextChanged += new System.EventHandler(this.txtTeamFilter_TextChanged);

            this.lvTeams.Dock = DockStyle.Fill;
            this.lvTeams.View = View.Details;
            this.lvTeams.FullRowSelect = true;
            this.lvTeams.MultiSelect = true;
            this.lvTeams.HideSelection = false;
            this.lvTeams.Columns.Add("Team", 220);
            this.lvTeams.Columns.Add("Business Unit", 180);
            this.lvTeams.Columns.Add("Type", 130);

            this.split.Panel2.Controls.Add(this.lvTeams);
            this.split.Panel2.Controls.Add(this.txtTeamFilter);
            this.split.Panel2.Controls.Add(this.lblTeams);

            // ---- StatusStrip ----
            this.lblStatus.Text = "Click \"Load / Refresh\" after connecting to an environment.";
            this.statusStrip.Items.Add(this.lblStatus);

            // ---- Control ----
            this.Controls.Add(this.split);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.Name = "BuMatrixSecurityRoleAssignerControl";
            this.Size = new System.Drawing.Size(820, 560);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // Small toolbar glyph rendered at runtime, avoiding a shipped image resource.
        private static Image CreateGlyphIcon(string symbol, Color color)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", 11f, FontStyle.Bold))
            using (var brush = new SolidBrush(color))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                g.Clear(Color.Transparent);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(symbol, font, brush, new RectangleF(0, 0, 16, 16), sf);
            }
            return bmp;
        }
    }
}
