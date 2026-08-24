using System;
using System.Collections.Generic;
using System.Linq;

namespace BuMatrixSecurityRoleAssigner.Core
{
    /// <summary>
    /// Click-to-sort state for one grid: which column the user sorted by and in which direction,
    /// plus the ordering and header decoration that follow from it (issue #23).
    /// <para>
    /// Lives in Core - and talks in row/column/cell-text terms rather than WinForms ones - so the
    /// ordering rules are unit-testable without a UI. The control owns one instance per grid and
    /// re-applies it every time the grid is repopulated, so a sort survives filter keystrokes and
    /// the Teams/Users toggle.
    /// </para>
    /// <para>
    /// Until a header is clicked no ordering is imposed at all: the lists arrive from
    /// <see cref="TeamRoleAssignmentService"/> already in a sensible default order, and silently
    /// re-sorting them would just be a different default.
    /// </para>
    /// </summary>
    public sealed class GridSort
    {
        /// <summary><see cref="Column"/> while the user hasn't sorted this grid yet.</summary>
        public const int NoColumn = -1;

        private const string AscendingIndicator = " \u25b2";    // BLACK UP-POINTING TRIANGLE
        private const string DescendingIndicator = " \u25bc";   // BLACK DOWN-POINTING TRIANGLE
        private static readonly char[] IndicatorTrim = { ' ', '\u25b2', '\u25bc' };

        private static readonly StringComparer CellComparer = StringComparer.OrdinalIgnoreCase;

        /// <summary>Index of the sorted column, or <see cref="NoColumn"/> if the user hasn't sorted yet.</summary>
        public int Column { get; private set; } = NoColumn;

        /// <summary>Sort direction of <see cref="Column"/>. Meaningless while <see cref="Column"/> is <see cref="NoColumn"/>.</summary>
        public bool Ascending { get; private set; } = true;

        /// <summary>
        /// Records a click on a column header: a new column starts ascending, the column already
        /// sorted flips direction.
        /// </summary>
        public void HeaderClicked(int column)
        {
            Ascending = column != Column || !Ascending;
            Column = column;
        }

        /// <summary>
        /// Orders <paramref name="rows"/> by the sorted column, reading each row's cell text
        /// through <paramref name="cell"/> (row, column index) - the same text the grid shows, so
        /// what the user sorts is what the user sees. Null cells sort as empty.
        /// <para>
        /// Ties break on the first column (the name), always ascending, so that sorting by a
        /// coarse column - business unit, team type - groups rows without scrambling the names
        /// inside each group.
        /// </para>
        /// </summary>
        public IEnumerable<T> Apply<T>(IEnumerable<T> rows, Func<T, int, string> cell)
        {
            if (Column == NoColumn) return rows;

            Func<T, string> key = r => cell(r, Column) ?? string.Empty;
            var ordered = Ascending
                ? rows.OrderBy(key, CellComparer)
                : rows.OrderByDescending(key, CellComparer);

            return Column == 0
                ? ordered
                : ordered.ThenBy(r => cell(r, 0) ?? string.Empty, CellComparer);
        }

        /// <summary>
        /// Returns <paramref name="header"/> with a direction arrow if <paramref name="column"/>
        /// is the sorted one, and without one otherwise. Any indicator already on the passed-in
        /// text is stripped first, so headers can be re-decorated in place (they are also
        /// rewritten by the Teams/Users toggle) without arrows piling up.
        /// </summary>
        public string DecorateHeader(string header, int column)
        {
            var bare = (header ?? string.Empty).TrimEnd(IndicatorTrim);
            if (column != Column) return bare;
            return bare + (Ascending ? AscendingIndicator : DescendingIndicator);
        }
    }
}
