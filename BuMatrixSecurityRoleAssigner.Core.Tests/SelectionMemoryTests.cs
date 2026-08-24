using System.Linq;
using Xunit;

namespace BuMatrixSecurityRoleAssigner.Core.Tests
{
    public class SelectionMemoryTests
    {
        [Fact]
        public void Capture_KeepsSelectedItemsThatAreTemporarilyHidden()
        {
            var memory = new SelectionMemory<string>();
            memory.Capture(new[] { "Ada", "Grace" }, new[] { "Ada" });
            memory.Capture(new[] { "Grace" }, new string[0]);

            Assert.Equal(new[] { "Ada" }, memory.Selected(new[] { "Ada", "Grace" }).ToArray());
        }

        [Fact]
        public void Capture_RemovesAnItemWhenItIsVisibleButNoLongerSelected()
        {
            var memory = new SelectionMemory<string>();
            memory.Capture(new[] { "Ada", "Grace" }, new[] { "Ada", "Grace" });
            memory.Capture(new[] { "Ada", "Grace" }, new[] { "Grace" });

            Assert.Equal(new[] { "Grace" }, memory.Selected(new[] { "Ada", "Grace" }).ToArray());
        }

        [Fact]
        public void Selected_OnlyReturnsItemsCurrentlyInTheVisibleRows()
        {
            var memory = new SelectionMemory<string>();
            memory.Capture(new[] { "Ada", "Grace" }, new[] { "Ada", "Grace" });

            Assert.Equal(new[] { "Grace" }, memory.Selected(new[] { "Grace" }).ToArray());
        }
    }
}
