using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace BuMatrixSecurityRoleAssigner.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="GridSort"/> - the click-to-sort state behind both grids (issue #23).
    /// Rows here are plain string arrays standing in for a grid row's cells, which is all
    /// GridSort ever sees.
    /// </summary>
    public class GridSortTests
    {
        private static readonly List<string[]> Rows = new List<string[]>
        {
            new[] { "charlie", "Sales" },
            new[] { "alpha",   "Service" },
            new[] { "Bravo",   "Sales" },
        };

        private static string Cell(string[] row, int column) => row[column];

        private static string[] SortedNames(GridSort sort) =>
            sort.Apply(Rows, (System.Func<string[], int, string>)Cell).Select(r => r[0]).ToArray();

        [Fact]
        public void Apply_LeavesRowsInSourceOrder_UntilAColumnIsClicked()
        {
            var sort = new GridSort();

            Assert.Equal(new[] { "charlie", "alpha", "Bravo" }, SortedNames(sort));
        }

        [Fact]
        public void HeaderClicked_FirstClickOnAColumn_SortsAscending()
        {
            var sort = new GridSort();
            sort.HeaderClicked(0);

            Assert.True(sort.Ascending);
            Assert.Equal(0, sort.Column);
            Assert.Equal(new[] { "alpha", "Bravo", "charlie" }, SortedNames(sort));
        }

        [Fact]
        public void HeaderClicked_SameColumnTwice_TogglesToDescending()
        {
            var sort = new GridSort();
            sort.HeaderClicked(0);
            sort.HeaderClicked(0);

            Assert.False(sort.Ascending);
            Assert.Equal(new[] { "charlie", "Bravo", "alpha" }, SortedNames(sort));
        }

        [Fact]
        public void HeaderClicked_SameColumnThreeTimes_TogglesBackToAscending()
        {
            var sort = new GridSort();
            sort.HeaderClicked(0);
            sort.HeaderClicked(0);
            sort.HeaderClicked(0);

            Assert.True(sort.Ascending);
        }

        [Fact]
        public void HeaderClicked_ADifferentColumn_StartsAscendingAgain()
        {
            var sort = new GridSort();
            sort.HeaderClicked(0);
            sort.HeaderClicked(0);   // column 0 now descending
            sort.HeaderClicked(1);

            Assert.Equal(1, sort.Column);
            Assert.True(sort.Ascending);
        }

        [Fact]
        public void Apply_SortsCaseInsensitively()
        {
            var sort = new GridSort();
            sort.HeaderClicked(0);

            // "Bravo" sorts between "alpha" and "charlie", not ahead of both as an ordinal
            // (case-sensitive) comparison would put it.
            Assert.Equal(new[] { "alpha", "Bravo", "charlie" }, SortedNames(sort));
        }

        [Fact]
        public void Apply_TieBreaksOnTheFirstColumn()
        {
            var sort = new GridSort();
            sort.HeaderClicked(1);

            // Both Sales rows come first; within the group the name column decides.
            Assert.Equal(new[] { "Bravo", "charlie", "alpha" }, SortedNames(sort));
        }

        [Fact]
        public void Apply_KeepsTheTieBreakAscending_WhenTheSortIsDescending()
        {
            var sort = new GridSort();
            sort.HeaderClicked(1);
            sort.HeaderClicked(1);

            // Service first (descending), then the Sales group still A-Z by name.
            Assert.Equal(new[] { "alpha", "Bravo", "charlie" }, SortedNames(sort));
        }

        [Fact]
        public void Apply_TreatsNullCellsAsEmpty()
        {
            var rows = new List<string[]>
            {
                new[] { "b", "Yes" },
                new[] { "a", null },
            };
            var sort = new GridSort();
            sort.HeaderClicked(1);

            var names = sort.Apply(rows, (System.Func<string[], int, string>)Cell).Select(r => r[0]).ToArray();
            Assert.Equal(new[] { "a", "b" }, names);
        }

        [Fact]
        public void DecorateHeader_LeavesHeadersAloneUntilAColumnIsClicked()
        {
            var sort = new GridSort();

            Assert.Equal("Team", sort.DecorateHeader("Team", 0));
            Assert.Equal("Business Unit", sort.DecorateHeader("Business Unit", 1));
        }

        [Fact]
        public void DecorateHeader_MarksOnlyTheSortedColumn()
        {
            var sort = new GridSort();
            sort.HeaderClicked(0);

            Assert.Equal("Team \u25b2", sort.DecorateHeader("Team", 0));
            Assert.Equal("Business Unit", sort.DecorateHeader("Business Unit", 1));
        }

        [Fact]
        public void DecorateHeader_ShowsADownArrow_WhenDescending()
        {
            var sort = new GridSort();
            sort.HeaderClicked(0);
            sort.HeaderClicked(0);

            Assert.Equal("Team \u25bc", sort.DecorateHeader("Team", 0));
        }

        [Fact]
        public void DecorateHeader_ReplacesAnIndicatorLeftOnTheHeaderFromAnEarlierSort()
        {
            var sort = new GridSort();
            sort.HeaderClicked(1);

            // Headers are re-decorated in place, so what comes back in is last round's text.
            Assert.Equal("Team", sort.DecorateHeader("Team \u25b2", 0));
            Assert.Equal("Business Unit \u25b2", sort.DecorateHeader("Business Unit \u25bc", 1));
        }
    }
}
