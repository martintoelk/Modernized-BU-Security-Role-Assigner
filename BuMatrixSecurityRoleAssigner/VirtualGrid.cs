using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using BuMatrixSecurityRoleAssigner.Core;

namespace BuMatrixSecurityRoleAssigner
{
    /// <summary>One grid row: the cell texts as shown, plus the model the row stands for.</summary>
    internal sealed class GridRow
    {
        public GridRow(object model, params string[] cells)
        {
            Model = model;
            Cells = cells ?? new string[0];
        }

        public object Model { get; }

        public string[] Cells { get; }

        /// <summary>Cell text for a column, or empty for a column this row doesn't reach.</summary>
        public string Cell(int column) =>
            column >= 0 && column < Cells.Length ? Cells[column] ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Binds one <see cref="ListView"/> in <see cref="ListView.VirtualMode"/> to a list of rows
    /// and a <see cref="SelectionMemory{T}"/> of checked models.
    /// <para>
    /// Virtual mode is what makes the tool usable on a large tenant. A classic ListView holds one
    /// native item per row, so every repopulation - and the grids are repopulated on each filter
    /// keystroke, each sort and the Teams/Users toggle - costs thousands of native calls:
    /// measured on 30,000 users, one filter keystroke with everything checked took ~13 s
    /// (6.4 s of it <c>Items.AddRange</c>, 5.8 s re-checking rows), and a single checkbox click
    /// ~280 ms - the tool was effectively frozen. In virtual mode the control holds no rows at
    /// all: it asks for the handful it is painting via <see cref="ListView.RetrieveVirtualItem"/>,
    /// so the same keystroke is a list swap and a repaint - ~2 ms, flat in the number of rows.
    /// See docs/research/large-tenant-grid-performance.md.
    /// </para>
    /// <para>
    /// The trade is that the control keeps no check state of its own: <see cref="ListView.CheckedItems"/>
    /// and <see cref="ListView.CheckedIndices"/> throw in virtual mode, ItemCheck/ItemChecked
    /// never fire, and clicking a checkbox glyph does nothing by itself. Checking is therefore
    /// driven from here - mouse and keyboard both land in Toggle, which writes to the memory and
    /// redraws the affected rows so the glyphs follow it.
    /// </para>
    /// </summary>
    internal sealed class VirtualGrid
    {
        private readonly ListView _lv;
        private readonly GridSort _sort;
        private List<GridRow> _rows = new List<GridRow>();

        public VirtualGrid(ListView lv, GridSort sort, SelectionMemory<object> checkedRows)
        {
            _lv = lv ?? throw new ArgumentNullException(nameof(lv));
            _sort = sort ?? throw new ArgumentNullException(nameof(sort));
            Checked = checkedRows ?? throw new ArgumentNullException(nameof(checkedRows));

            _lv.VirtualMode = true;
            _lv.VirtualListSize = 0;
            _lv.RetrieveVirtualItem += OnRetrieveVirtualItem;
            _lv.MouseClick += OnMouseClick;
            _lv.KeyDown += OnKeyDown;
        }

        /// <summary>The checked rows, by model - including rows the current filter hides.</summary>
        public SelectionMemory<object> Checked { get; }

        /// <summary>How many rows the grid is currently showing (after filtering).</summary>
        public int ShownCount => _rows.Count;

        /// <summary>Raised whenever a check changes, so the caller can refresh its status line.</summary>
        public event EventHandler CheckedChanged;

        /// <summary>
        /// Replaces the shown rows, applying the grid's sort and refreshing the header arrows.
        /// Checks are not touched: they live in <see cref="Checked"/> by model identity, so they
        /// survive a filter that hides the row and reappear with it.
        /// </summary>
        public void Show(IEnumerable<GridRow> rows)
        {
            var next = _sort.Apply(rows, (r, column) => r.Cell(column)).ToList();

            // Shrink the control's idea of the list before swapping the rows in: a repaint landing
            // between the two would otherwise ask for an old index against the new, shorter list.
            _lv.VirtualListSize = 0;
            _rows = next;
            _lv.VirtualListSize = _rows.Count;

            for (var i = 0; i < _lv.Columns.Count; i++)
                _lv.Columns[i].Text = _sort.DecorateHeader(_lv.Columns[i].Text, i);

            _lv.Invalidate();

            // Keeps a checked row in view when the sort or filter moved it far down the list.
            var firstChecked = _rows.FindIndex(r => Checked.Contains(r.Model));
            if (firstChecked >= 0) _lv.EnsureVisible(firstChecked);
        }

        /// <summary>The models behind the rows currently shown, in the order shown.</summary>
        public IEnumerable<object> ShownModels => _rows.Select(r => r.Model);

        /// <summary>
        /// Checks every row the grid is currently showing - not every row loaded. Rows the filter
        /// box is hiding keep whatever state they had, so "Select All" on a filtered list can't
        /// quietly enlist rows the user can't see into an Add/Remove batch.
        /// </summary>
        public void CheckAllShown()
        {
            Checked.SelectAll(ShownModels);
            Redraw();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Empties the whole selection, including rows the filter is hiding - the deliberate
        /// asymmetry with <see cref="CheckAllShown"/>, because dropping rows from a pending batch
        /// is the safe direction to be generous in.
        /// </summary>
        public void ClearChecks()
        {
            Checked.Clear();
            Redraw();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Forgets the checks and the rows, for a fresh load.</summary>
        public void Reset()
        {
            Checked.Clear();
            Show(new GridRow[0]);
        }

        private void OnRetrieveVirtualItem(object sender, RetrieveVirtualItemEventArgs e)
        {
            // A repaint queued before the last Show can still ask for an index past the current
            // rows; answering with a blank row beats throwing from inside a paint.
            if (e.ItemIndex < 0 || e.ItemIndex >= _rows.Count)
            {
                e.Item = new ListViewItem(string.Empty);
                return;
            }

            var row = _rows[e.ItemIndex];
            var item = new ListViewItem(row.Cell(0))
            {
                Tag = row.Model,
                Checked = Checked.Contains(row.Model)
            };
            for (var i = 1; i < row.Cells.Length; i++)
                item.SubItems.Add(row.Cells[i] ?? string.Empty);
            e.Item = item;
        }

        // A single left click anywhere on a row toggles that row's checkbox: the checkbox is the
        // only thing Add/Remove/Inspect act on (issue #25), so the whole row is the natural hit
        // target rather than a pixel-hunt down the left edge. Unlike a classic ListView, the
        // virtual one toggles nothing itself - not on the glyph, not on a double click - so every
        // location toggles here, and a double click (MouseClick, then a MouseDoubleClick that is
        // deliberately not subscribed) stays worth exactly one toggle.
        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;

            var hit = _lv.HitTest(e.Location);
            if (hit.Item == null) return;

            Toggle(new[] { hit.Item.Index });
        }

        // Space toggles the highlighted row(s) - something the classic control does for itself,
        // but the virtual one does not.
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Space) return;

            var indices = _lv.SelectedIndices.Cast<int>().ToArray();
            if (indices.Length == 0) return;

            Toggle(indices);
            e.Handled = true;
        }

        /// <summary>
        /// Flips the checks on the given shown rows and redraws them. A multi-row toggle follows
        /// the first row, so a block of mixed rows ends up uniformly checked or unchecked rather
        /// than inverted row by row.
        /// </summary>
        private void Toggle(IList<int> indices)
        {
            var inRange = indices.Where(i => i >= 0 && i < _rows.Count).ToArray();
            if (inRange.Length == 0) return;

            var check = !Checked.Contains(_rows[inRange[0]].Model);
            foreach (var index in inRange)
                Checked.Set(_rows[index].Model, check);

            Redraw(inRange.Min(), inRange.Max());
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }

        // Virtual rows are re-fetched rather than repainted from a cache, so a redraw is what
        // makes the glyphs agree with the memory again.
        private void Redraw() => Redraw(0, _rows.Count - 1);

        private void Redraw(int from, int to)
        {
            if (_rows.Count == 0 || to < from) return;
            _lv.RedrawItems(Math.Max(from, 0), Math.Min(to, _rows.Count - 1), false);
        }
    }
}
