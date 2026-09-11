using System.Linq;
using Xunit;

namespace BuMatrixSecurityRoleAssigner.Core.Tests
{
    public class SelectionMemoryTests
    {
        [Fact]
        public void Set_RemembersAndForgetsIndividualRows()
        {
            var memory = new SelectionMemory<string>();
            memory.Set("Ada", true);
            memory.Set("Grace", true);
            memory.Set("Ada", false);

            Assert.False(memory.Contains("Ada"));
            Assert.True(memory.Contains("Grace"));
            Assert.Equal(1, memory.Count);
        }

        [Fact]
        public void Toggle_FlipsTheRowAndReportsTheStateItEndedUpIn()
        {
            var memory = new SelectionMemory<string>();

            Assert.True(memory.Toggle("Ada"));
            Assert.True(memory.Contains("Ada"));
            Assert.False(memory.Toggle("Ada"));
            Assert.False(memory.Contains("Ada"));
        }

        [Fact]
        public void SelectAll_AddsTheSuppliedRowsAndLeavesTheRestAlone()
        {
            var memory = new SelectionMemory<string>();
            memory.Set("Ada", true);

            // What "Select All" passes in: the rows a filter is currently showing.
            memory.SelectAll(new[] { "Grace", "Katherine" });

            Assert.Equal(new[] { "Ada", "Grace", "Katherine" },
                memory.Selected(new[] { "Ada", "Grace", "Katherine" }).ToArray());
        }

        [Fact]
        public void Count_IncludesRowsTheFilterIsHiding()
        {
            var memory = new SelectionMemory<string>();
            memory.SelectAll(new[] { "Ada", "Grace" });

            // "Grace" is all the grid is showing, but both rows are still in the pending batch -
            // which is what the status line's "N checked" reports.
            Assert.Equal(2, memory.Count);
            Assert.Equal(new[] { "Grace" }, memory.Selected(new[] { "Grace" }).ToArray());
        }

        [Fact]
        public void Selected_OnlyReturnsItemsPresentInTheSuppliedRows()
        {
            var memory = new SelectionMemory<string>();
            memory.SelectAll(new[] { "Ada", "Grace" });

            Assert.Equal(new[] { "Grace" }, memory.Selected(new[] { "Grace" }).ToArray());
        }

        [Fact]
        public void Clear_ForgetsEverything_IncludingHiddenRows()
        {
            var memory = new SelectionMemory<string>();
            memory.SelectAll(new[] { "Ada", "Grace" });

            memory.Clear();

            Assert.Equal(0, memory.Count);
            Assert.Empty(memory.Selected(new[] { "Ada", "Grace" }));
        }
    }
}
