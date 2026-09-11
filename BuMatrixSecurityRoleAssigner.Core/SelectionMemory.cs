using System.Collections.Generic;
using System.Linq;

namespace BuMatrixSecurityRoleAssigner.Core
{
    /// <summary>
    /// The authoritative record of which rows are checked, by model identity rather than by grid
    /// row. The grids render their checkboxes from this (they are virtual-mode ListViews that
    /// hold no per-row state of their own - see VirtualGrid), so a row the filter box is hiding
    /// stays part of the pending batch, and a filter keystroke costs nothing per remembered row.
    /// </summary>
    public sealed class SelectionMemory<T>
    {
        private readonly HashSet<T> _selected = new HashSet<T>();

        /// <summary>How many rows are checked, including any the current filter hides.</summary>
        public int Count => _selected.Count;

        /// <summary>Whether the row identity is currently checked.</summary>
        public bool Contains(T item) => _selected.Contains(item);

        /// <summary>Checks or unchecks one row.</summary>
        public void Set(T item, bool selected)
        {
            if (selected) _selected.Add(item);
            else _selected.Remove(item);
        }

        /// <summary>Flips one row's check, returning the state it ended up in.</summary>
        public bool Toggle(T item)
        {
            var selected = !_selected.Contains(item);
            Set(item, selected);
            return selected;
        }

        /// <summary>Checks every supplied row, leaving the rest of the memory alone.</summary>
        public void SelectAll(IEnumerable<T> items)
        {
            foreach (var item in items)
                _selected.Add(item);
        }

        /// <summary>
        /// Returns the checked rows that are present in the supplied rows, in the supplied order.
        /// Used to turn the remembered identities back into a batch of models to act on.
        /// </summary>
        public IEnumerable<T> Selected(IEnumerable<T> items) => items.Where(_selected.Contains);

        /// <summary>Forgets all checked rows, such as when a fresh data load replaces the cache.</summary>
        public void Clear() => _selected.Clear();
    }
}
