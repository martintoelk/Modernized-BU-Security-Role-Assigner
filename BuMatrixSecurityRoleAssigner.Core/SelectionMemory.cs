using System.Collections.Generic;
using System.Linq;

namespace BuMatrixSecurityRoleAssigner.Core
{
    /// <summary>
    /// Remembers selected row identities while a filtered grid is rebuilt. Items that are
    /// currently visible but no longer selected are removed; hidden items are left alone so
    /// temporarily filtering them out does not change the pending selection.
    /// </summary>
    public sealed class SelectionMemory<T>
    {
        private readonly HashSet<T> _selected = new HashSet<T>();

        /// <summary>
        /// Synchronizes the memory with the currently visible portion of a grid.
        /// </summary>
        public void Capture(IEnumerable<T> visibleItems, IEnumerable<T> selectedVisibleItems)
        {
            foreach (var item in visibleItems)
                _selected.Remove(item);

            foreach (var item in selectedVisibleItems)
                _selected.Add(item);
        }

        /// <summary>
        /// Returns the remembered selections that are present in the supplied visible rows.
        /// The visible-row order is preserved.
        /// </summary>
        public IEnumerable<T> Selected(IEnumerable<T> visibleItems) =>
            visibleItems.Where(_selected.Contains);

        /// <summary>Forgets all remembered rows, such as when a fresh data load replaces the cache.</summary>
        public void Clear() => _selected.Clear();

        /// <summary>Whether the row identity is currently remembered as selected.</summary>
        public bool Contains(T item) => _selected.Contains(item);
    }
}
