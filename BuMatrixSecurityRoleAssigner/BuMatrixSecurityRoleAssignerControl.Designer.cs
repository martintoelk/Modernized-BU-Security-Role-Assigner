using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BuMatrixSecurityRoleAssigner
{
    partial class BuMatrixSecurityRoleAssignerControl
    {
        private ToolStrip toolStrip;
        private ToolStripButton tsbLoad;
        private CheckBox chkRemoveAllBus;
        private ToolStripControlHost tshRemoveAllBus;

        // Hosted above the Teams/Users list (not the main toolbar) so it reads as "which list
        // is this" rather than a general command.
        private ToolStrip modeStrip;
        private ToolStripButton tsbUsersMode;

        // Mirrors modeStrip above the Roles list, so both grid headers sit at the same height
        // instead of the roles side floating higher once the mode strip appears on the other
        // side. Read-only (no toggle - the BU mode is auto-detected, not user-chosen).
        private ToolStrip modeStripRoles;
        private ToolStripLabel lblBuModeIndicator;

        private TableLayoutPanel mainTable;

        private Label lblTeams;
        private TextBox txtTeamFilter;
        private ListView lvTeams;

        private Label lblRoles;
        private TextBox txtRoleFilter;
        private ListView lvRoles;

        // Middle column between the two grids.
        private TableLayoutPanel buttonPanel;
        private Button btnAdd;
        private Button btnRemove;

        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblStatus;

        private void InitializeComponent()
        {
            this.toolStrip = new ToolStrip();
            this.tsbLoad = new ToolStripButton();
            this.chkRemoveAllBus = new CheckBox();

            this.modeStrip = new ToolStrip();
            this.tsbUsersMode = new ToolStripButton();

            this.modeStripRoles = new ToolStrip();
            this.lblBuModeIndicator = new ToolStripLabel();

            this.mainTable = new TableLayoutPanel();

            this.lblTeams = new Label();
            this.txtTeamFilter = new TextBox();
            this.lvTeams = new ListView();

            this.lblRoles = new Label();
            this.txtRoleFilter = new TextBox();
            this.lvRoles = new ListView();

            this.buttonPanel = new TableLayoutPanel();
            this.btnAdd = new Button();
            this.btnRemove = new Button();

            this.statusStrip = new StatusStrip();
            this.lblStatus = new ToolStripStatusLabel();

            this.SuspendLayout();

            // ---- ToolStrip ----
            this.tsbLoad.Text = "Load / Refresh";
            this.tsbLoad.Image = CreateRefreshIcon();
            this.tsbLoad.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            this.tsbLoad.Click += new System.EventHandler(this.tsbLoad_Click);

            // Mode toggle: off = Teams (default), on = Users. Switching swaps the target list's
            // contents and columns - the two modes are never mixed in one Add/Remove call.
            // Hosted in its own strip above the Teams/Users list header (not the main toolbar),
            // so it reads as "which list is this" rather than a general command.
            this.tsbUsersMode.Text = "Mode: Teams";
            this.tsbUsersMode.Image = CreateTeamsIcon();
            this.tsbUsersMode.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            this.tsbUsersMode.CheckOnClick = true;
            this.tsbUsersMode.Checked = false;
            this.tsbUsersMode.ToolTipText = "Toggle between assigning roles to Teams or to Users.";
            this.tsbUsersMode.CheckedChanged += new System.EventHandler(this.tsbUsersMode_CheckedChanged);

            this.modeStrip.Items.Add(this.tsbUsersMode);
            this.modeStrip.Dock = DockStyle.Top;
            this.modeStrip.GripStyle = ToolStripGripStyle.Hidden;

            // Read-only counterpart on the Roles side: shows whether the connected org is running
            // Modernized BUs or Classic BUs (auto-detected via a behavioral probe, see
            // GetModernizedBuStatus - never a manual toggle). Its only job here is to occupy the
            // same strip height as modeStrip so lblRoles and lblTeams line up.
            this.lblBuModeIndicator.Text = "Mode: Unknown";
            this.lblBuModeIndicator.Image = CreateUnknownBuIcon();
            this.lblBuModeIndicator.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            this.lblBuModeIndicator.ToolTipText =
                "Whether the connected environment uses Modernized (matrix) business units or Classic " +
                "business units. Auto-detected on Load / Refresh - not a manual setting.";

            this.modeStripRoles.Items.Add(this.lblBuModeIndicator);
            this.modeStripRoles.Dock = DockStyle.Top;
            this.modeStripRoles.GripStyle = ToolStripGripStyle.Hidden;

            // Remove-only opt-in, shown as a real checkbox (not a toggle button). Off (default):
            // remove only the exact role row(s) selected, i.e. just that business-unit copy. On:
            // for each selected role, remove EVERY business-unit copy of that role currently
            // assigned to the selected team(s)/user(s).
            this.chkRemoveAllBus.Text = "Remove from all BUs";
            this.chkRemoveAllBus.AutoSize = true;
            this.chkRemoveAllBus.Padding = new Padding(4, 0, 4, 0);

            this.tshRemoveAllBus = new ToolStripControlHost(this.chkRemoveAllBus)
            {
                ToolTipText =
                    "Remove only (ignored when adding). Off (default): remove only the exact role row(s) " +
                    "you selected, i.e. just that business-unit copy.\r\n" +
                    "On: for each selected role, remove EVERY business-unit copy of that role currently " +
                    "assigned to the selected team(s)/user(s)."
            };

            this.toolStrip.Items.AddRange(new ToolStripItem[]
            {
                this.tsbLoad, this.tshRemoveAllBus
            });
            this.toolStrip.Location = new System.Drawing.Point(0, 0);
            this.toolStrip.GripStyle = ToolStripGripStyle.Hidden;

            // ---- Main 3-column layout: Roles | Add/Remove buttons | Teams ----
            this.mainTable.Dock = DockStyle.Fill;
            this.mainTable.ColumnCount = 3;
            this.mainTable.RowCount = 1;
            this.mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            this.mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180f));
            this.mainTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            this.mainTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            // Left: Roles
            this.lblRoles.Text = "Security roles (multi-select) - Business Unit shown";
            this.lblRoles.Dock = DockStyle.Top;
            this.lblRoles.Height = 20;
            this.lblRoles.Padding = new Padding(2, 3, 0, 0);

            this.txtRoleFilter.Dock = DockStyle.Fill;
            this.txtRoleFilter.BorderStyle = BorderStyle.None;
            this.txtRoleFilter.TextChanged += new System.EventHandler(this.txtRoleFilter_TextChanged);
            var roleFilterBox = CreateSearchBox(this.txtRoleFilter);

            this.lvRoles.Dock = DockStyle.Fill;
            this.lvRoles.View = View.Details;
            this.lvRoles.FullRowSelect = true;
            this.lvRoles.MultiSelect = true;
            this.lvRoles.HideSelection = false;
            this.lvRoles.Columns.Add("Security Role", 240);
            this.lvRoles.Columns.Add("Business Unit", 200);

            var rolesPanel = new Panel { Dock = DockStyle.Fill };
            // NOTE: add order = docked controls draw top-most last, so add Fill first, then the Top items.
            rolesPanel.Controls.Add(this.lvRoles);
            rolesPanel.Controls.Add(roleFilterBox);
            rolesPanel.Controls.Add(this.lblRoles);
            // Added last so it docks above lblRoles, mirroring modeStrip above lblTeams.
            rolesPanel.Controls.Add(this.modeStripRoles);
            this.mainTable.Controls.Add(rolesPanel, 0, 0);

            // Middle: Add / Remove buttons, vertically centered between the two grids.
            this.btnAdd.Text = "Add roles to team(s)";
            this.btnAdd.Image = CreateBadgeIcon(plus: true, Color.FromArgb(0, 140, 0));
            this.btnAdd.ImageAlign = ContentAlignment.MiddleLeft;
            this.btnAdd.TextAlign = ContentAlignment.MiddleCenter;
            this.btnAdd.TextImageRelation = TextImageRelation.ImageBeforeText;
            this.btnAdd.AutoSize = false;
            this.btnAdd.Size = new System.Drawing.Size(170, 34);
            this.btnAdd.Anchor = AnchorStyles.None;
            this.btnAdd.Click += new System.EventHandler(this.btnAdd_Click);

            this.btnRemove.Text = "Remove roles from team(s)";
            this.btnRemove.Image = CreateBadgeIcon(plus: false, Color.FromArgb(180, 0, 0));
            this.btnRemove.ImageAlign = ContentAlignment.MiddleLeft;
            this.btnRemove.TextAlign = ContentAlignment.MiddleCenter;
            this.btnRemove.TextImageRelation = TextImageRelation.ImageBeforeText;
            this.btnRemove.AutoSize = false;
            this.btnRemove.Size = new System.Drawing.Size(170, 34);
            this.btnRemove.Anchor = AnchorStyles.None;
            this.btnRemove.Click += new System.EventHandler(this.btnRemove_Click);

            // Two spacer rows (equal Percent weight) above/below the buttons keep them centered
            // vertically regardless of the panel's height.
            this.buttonPanel.Dock = DockStyle.Fill;
            this.buttonPanel.ColumnCount = 1;
            this.buttonPanel.RowCount = 4;
            this.buttonPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            this.buttonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            this.buttonPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.buttonPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            this.buttonPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            this.buttonPanel.Controls.Add(this.btnAdd, 0, 1);
            this.buttonPanel.Controls.Add(this.btnRemove, 0, 2);
            this.mainTable.Controls.Add(this.buttonPanel, 1, 0);

            // Right: Teams
            this.lblTeams.Text = "Teams (multi-select)";
            this.lblTeams.Dock = DockStyle.Top;
            this.lblTeams.Height = 20;
            this.lblTeams.Padding = new Padding(2, 3, 0, 0);

            this.txtTeamFilter.Dock = DockStyle.Fill;
            this.txtTeamFilter.BorderStyle = BorderStyle.None;
            this.txtTeamFilter.TextChanged += new System.EventHandler(this.txtTeamFilter_TextChanged);
            var teamFilterBox = CreateSearchBox(this.txtTeamFilter);

            this.lvTeams.Dock = DockStyle.Fill;
            this.lvTeams.View = View.Details;
            this.lvTeams.FullRowSelect = true;
            this.lvTeams.MultiSelect = true;
            this.lvTeams.HideSelection = false;
            this.lvTeams.Columns.Add("Team", 220);
            this.lvTeams.Columns.Add("Business Unit", 180);
            this.lvTeams.Columns.Add("Type", 130);

            var teamsPanel = new Panel { Dock = DockStyle.Fill };
            teamsPanel.Controls.Add(this.lvTeams);
            teamsPanel.Controls.Add(teamFilterBox);
            teamsPanel.Controls.Add(this.lblTeams);
            // Added last so it docks above lblTeams (later Top-docked additions render outward).
            teamsPanel.Controls.Add(this.modeStrip);
            this.mainTable.Controls.Add(teamsPanel, 2, 0);

            // ---- StatusStrip ----
            this.lblStatus.Text = "Click \"Load / Refresh\" after connecting to an environment.";
            this.statusStrip.Items.Add(this.lblStatus);

            // ---- Control ----
            this.Controls.Add(this.mainTable);
            this.Controls.Add(this.statusStrip);
            this.Controls.Add(this.toolStrip);
            this.Name = "BuMatrixSecurityRoleAssignerControl";
            this.Size = new System.Drawing.Size(820, 560);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        // Native "cue banner" placeholder text - shown only while the box is empty and unfocused,
        // and never interferes with the actual Text value used by the filter logic. Deferred to
        // HandleCreated (rather than sent immediately) so it doesn't force premature Win32 handle
        // creation on a still-unparented control, and so it's reapplied if the handle is ever
        // recreated (e.g. by host theming).
        private static void SetCueBanner(TextBox textBox, string text) =>
            textBox.HandleCreated += (s, e) => SendMessage(textBox.Handle, EM_SETCUEBANNER, IntPtr.Zero, text);

        // Wraps a filter TextBox with a magnifying-glass icon and a bordered frame so it reads as
        // a search box rather than a plain, purpose-unclear textbox. The caller must already have
        // set the TextBox's Dock to Fill and BorderStyle to None.
        private static Panel CreateSearchBox(TextBox textBox)
        {
            var icon = new PictureBox
            {
                Image = CreateSearchIcon(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Left,
                Cursor = Cursors.IBeam,
                BackColor = SystemColors.Window,
            };
            // Clicking the icon should focus the box, same as clicking the box itself.
            icon.Click += (s, e) => textBox.Focus();

            var container = new Panel
            {
                Dock = DockStyle.Top,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
            };
            // Add order = docked controls draw top-most last, so add Fill first, then Left.
            container.Controls.Add(textBox);
            container.Controls.Add(icon);

            // TextBox.Font is ambient (falls back to Control.DefaultFont until the control is
            // actually parented into a tree with a real Font, e.g. once the XTB host themes the
            // plugin), so size off FontChanged rather than computing PreferredHeight once here -
            // that would freeze in the wrong height if the host's font differs from the default.
            void SyncToFont(object s, EventArgs e)
            {
                container.Height = textBox.PreferredHeight + 2;
                icon.Width = container.Height;
            }
            textBox.FontChanged += SyncToFont;
            SyncToFont(null, EventArgs.Empty);

            SetCueBanner(textBox, "Search...");

            return container;
        }

        // Small circular +/- badge icon rendered at runtime, avoiding a shipped image resource.
        private static Image CreateBadgeIcon(bool plus, Color color)
        {
            var bmp = new Bitmap(20, 20);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, 1, 1, 18, 18);

                using (var pen = new Pen(Color.White, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, 5.5f, 10f, 14.5f, 10f);
                    if (plus)
                        g.DrawLine(pen, 10f, 5.5f, 10f, 14.5f);
                }
            }
            return bmp;
        }

        // Monochrome magnifying glass for the search-box icon; deliberately uncolored so it reads
        // as an inline glyph inside a white textbox rather than a toolbar badge.
        internal static Image CreateSearchIcon()
        {
            var bmp = new Bitmap(14, 14);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var pen = new Pen(Color.FromArgb(120, 120, 120), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawEllipse(pen, 1f, 1f, 7.5f, 7.5f);
                    g.DrawLine(pen, 8.0f, 8.0f, 12.5f, 12.5f);
                }
            }
            return bmp;
        }

        // Toolbar-sized (16x16) circular arrow, matching the Add/Remove badge style.
        internal static Image CreateRefreshIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(0, 110, 190)))
                    g.FillEllipse(brush, 0, 0, 16, 16);

                var rect = new RectangleF(3.5f, 3.5f, 9, 9);
                using (var pen = new Pen(Color.White, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, rect, -40, 260);

                // Arrowhead at the trailing end of the arc (-40 + 260 = 220 degrees).
                const double endAngle = 220 * Math.PI / 180;
                var cx = rect.X + rect.Width / 2;
                var cy = rect.Y + rect.Height / 2;
                var tipX = cx + rect.Width / 2 * Math.Cos(endAngle);
                var tipY = cy + rect.Height / 2 * Math.Sin(endAngle);
                var tip = new PointF((float)tipX, (float)tipY);
                var p1 = new PointF((float)(tipX - 2.6), (float)(tipY - 0.5));
                var p2 = new PointF((float)(tipX + 1.0), (float)(tipY - 2.3));
                using (var whiteBrush = new SolidBrush(Color.White))
                    g.FillPolygon(whiteBrush, new[] { p1, p2, tip });
            }
            return bmp;
        }

        // Two-person "group" glyph for Teams mode.
        internal static Image CreateTeamsIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(0, 150, 130)))
                    g.FillEllipse(brush, 0, 0, 16, 16);

                using (var white = new SolidBrush(Color.White))
                {
                    g.FillEllipse(white, 3.5f, 3.5f, 4, 4);
                    g.FillEllipse(white, 8.5f, 3.5f, 4, 4);
                    g.FillPie(white, 2.0f, 7.5f, 7, 7, 180, 180);
                    g.FillPie(white, 7.0f, 7.5f, 7, 7, 180, 180);
                }
            }
            return bmp;
        }

        // 2x2 grid glyph for "Modernized BUs" (matrix data-access model).
        internal static Image CreateModernizedBuIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(0, 110, 190)))
                    g.FillEllipse(brush, 0, 0, 16, 16);

                using (var white = new SolidBrush(Color.White))
                {
                    g.FillRectangle(white, 3.5f, 3.5f, 3.6f, 3.6f);
                    g.FillRectangle(white, 8.9f, 3.5f, 3.6f, 3.6f);
                    g.FillRectangle(white, 3.5f, 8.9f, 3.6f, 3.6f);
                    g.FillRectangle(white, 8.9f, 8.9f, 3.6f, 3.6f);
                }
            }
            return bmp;
        }

        // Small hierarchy/tree glyph for "Classic BUs" (single-BU-ownership model).
        internal static Image CreateClassicBuIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(170, 110, 0)))
                    g.FillEllipse(brush, 0, 0, 16, 16);

                using (var pen = new Pen(Color.White, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(pen, 8f, 4.5f, 8f, 8f);
                    g.DrawLine(pen, 4.5f, 8f, 11.5f, 8f);
                    g.DrawLine(pen, 4.5f, 8f, 4.5f, 11f);
                    g.DrawLine(pen, 11.5f, 8f, 11.5f, 11f);
                }
                using (var white = new SolidBrush(Color.White))
                {
                    g.FillEllipse(white, 6.4f, 2.6f, 3.2f, 3.2f);
                    g.FillEllipse(white, 2.9f, 9.4f, 3.2f, 3.2f);
                    g.FillEllipse(white, 9.9f, 9.4f, 3.2f, 3.2f);
                }
            }
            return bmp;
        }

        // Greyed-out "?" glyph shown before Load / Refresh has run its BU-mode probe.
        internal static Image CreateUnknownBuIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(150, 150, 150)))
                    g.FillEllipse(brush, 0, 0, 16, 16);

                using (var white = new SolidBrush(Color.White))
                using (var font = new Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Bold))
                {
                    var format = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };
                    g.DrawString("?", font, white, new RectangleF(0, -0.5f, 16, 16), format);
                }
            }
            return bmp;
        }

        // Single-person glyph for Users mode.
        internal static Image CreateUserIcon()
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var brush = new SolidBrush(Color.FromArgb(120, 80, 170)))
                    g.FillEllipse(brush, 0, 0, 16, 16);

                using (var white = new SolidBrush(Color.White))
                {
                    g.FillEllipse(white, 5.5f, 3.0f, 5, 5);
                    g.FillPie(white, 3.0f, 8.0f, 10, 9, 180, 180);
                }
            }
            return bmp;
        }
    }
}
