using System.Windows.Forms;

namespace BuMatrixSecurityRoleAssigner
{
    partial class BuMatrixSecurityRoleAssignerControl
    {
        private ToolStrip toolStrip;
        private ToolStripButton tsbLoad;
        private ToolStripButton tsbAdd;
        private ToolStripButton tsbRemove;
        private ToolStripSeparator sep1;

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
            this.sep1 = new ToolStripSeparator();
            this.tsbAdd = new ToolStripButton();
            this.tsbRemove = new ToolStripButton();

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

            this.tsbAdd.Text = "Add roles to team(s)";
            this.tsbAdd.DisplayStyle = ToolStripItemDisplayStyle.Text;
            this.tsbAdd.Click += new System.EventHandler(this.tsbAdd_Click);

            this.tsbRemove.Text = "Remove roles from team(s)";
            this.tsbRemove.DisplayStyle = ToolStripItemDisplayStyle.Text;
            this.tsbRemove.Click += new System.EventHandler(this.tsbRemove_Click);

            // Classic-BU handling is now auto-detected (behavioral probe in TeamRoleAssignmentService)
            // rather than a manual toggle - see AssignOrRemove and OperationLog.ClassicBuDetected.

            this.toolStrip.Items.AddRange(new ToolStripItem[]
            {
                this.tsbLoad, this.sep1, this.tsbAdd, this.tsbRemove
            });
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.GripStyle = ToolStripGripStyle.Hidden;

            // ---- SplitContainer (teams | roles) ----
            this.split.Dock = DockStyle.Fill;
            this.split.Orientation = Orientation.Vertical;
            this.split.SplitterWidth = 6;

            // Left: Teams
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

            // NOTE: add order = ListView draws top-most last, so add Fill first, then the Top items.
            this.split.Panel1.Controls.Add(this.lvTeams);
            this.split.Panel1.Controls.Add(this.txtTeamFilter);
            this.split.Panel1.Controls.Add(this.lblTeams);

            // Right: Roles
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

            this.split.Panel2.Controls.Add(this.lvRoles);
            this.split.Panel2.Controls.Add(this.txtRoleFilter);
            this.split.Panel2.Controls.Add(this.lblRoles);

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
    }
}
