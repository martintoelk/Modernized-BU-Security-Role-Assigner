using System;
using Xunit;

namespace BuMatrixSecurityRoleAssigner.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="RoleHandoff"/> - the string payload this tool hands to
    /// "User/Team Role Inspector" over XrmToolBox's message bus (issue #17).
    /// <para>
    /// The receiving tool lives in a separate repo and a separate assembly, so these tests are
    /// pinning down a wire format, not an in-process object contract: any change that breaks a
    /// round trip here breaks the handoff for an Inspector build that this repo never compiles
    /// against.
    /// </para>
    /// </summary>
    public class RoleHandoffTests
    {
        [Fact]
        public void RoundTrips_AUserHandoff()
        {
            var id = Guid.NewGuid();
            var buId = Guid.NewGuid();
            var payload = new RoleHandoff
            {
                Entity = "systemuser",
                Id = id,
                Name = "Ada Lovelace",
                BusinessUnitId = buId,
                BusinessUnitName = "Contoso"
            }.ToPayload();

            Assert.True(RoleHandoff.TryParse(payload, out var parsed));
            Assert.Equal("systemuser", parsed.Entity);
            Assert.Equal(id, parsed.Id);
            Assert.Equal("Ada Lovelace", parsed.Name);
            Assert.Equal(buId, parsed.BusinessUnitId);
            Assert.Equal("Contoso", parsed.BusinessUnitName);
        }

        [Fact]
        public void RoundTrips_ATeamHandoff()
        {
            var payload = RoleHandoff.ForTarget(new TeamItem
            {
                Id = Guid.NewGuid(),
                Name = "Sales Managers",
                BusinessUnitId = Guid.NewGuid(),
                BusinessUnitName = "Sales"
            }).ToPayload();

            Assert.True(RoleHandoff.TryParse(payload, out var parsed));
            Assert.Equal("team", parsed.Entity);
            Assert.Equal("Sales Managers", parsed.Name);
        }

        [Fact]
        public void ForTarget_MapsEachTargetKindToItsEntityLogicalName()
        {
            Assert.Equal("team", RoleHandoff.ForTarget(new TeamItem { Name = "T" }).Entity);
            Assert.Equal("systemuser", RoleHandoff.ForTarget(new UserItem { Name = "U" }).Entity);
        }

        // Names come straight out of Dataverse, so they can carry anything - including the very
        // characters the payload uses as separators.
        [Theory]
        [InlineData("A & B = C")]
        [InlineData("name?with&every=separator")]
        [InlineData("  leading and trailing  ")]
        [InlineData("Ünïcöde 名前")]
        [InlineData("")]
        public void RoundTrips_NamesContainingSeparatorsAndNonAsciiCharacters(string name)
        {
            var payload = new RoleHandoff { Entity = "team", Id = Guid.NewGuid(), Name = name }.ToPayload();

            Assert.True(RoleHandoff.TryParse(payload, out var parsed));
            Assert.Equal(name, parsed.Name);
        }

        [Fact]
        public void ParsesAHandoffWithNoBusinessUnitContext()
        {
            var payload = new RoleHandoff { Entity = "team", Id = Guid.NewGuid(), Name = "T" }.ToPayload();

            Assert.True(RoleHandoff.TryParse(payload, out var parsed));
            Assert.Null(parsed.BusinessUnitId);
            Assert.Null(parsed.BusinessUnitName);
        }

        // v stays 1 while the format only grows keys, so a receiver built before a key existed
        // must ignore it rather than reject the whole message.
        [Fact]
        public void IgnoresKeysItDoesNotKnow()
        {
            var id = Guid.NewGuid();

            Assert.True(RoleHandoff.TryParse($"xtbrolehandoff:v=1&entity=team&id={id}&name=T&somethingnew=x", out var parsed));
            Assert.Equal(id, parsed.Id);
        }

        [Theory]
        [InlineData(null)]                                          // no payload at all
        [InlineData("")]                                            // empty payload
        [InlineData("some other tool's string payload")]            // not ours - e.g. FetchXML
        [InlineData("xtbrolehandoff:v=1&entity=team")]              // no id
        [InlineData("xtbrolehandoff:v=1&entity=team&id=not-a-guid")]// unparsable id
        [InlineData("xtbrolehandoff:v=1&id=00000000-0000-0000-0000-000000000001")] // no entity
        [InlineData("xtbrolehandoff:v=2&entity=team&id=00000000-0000-0000-0000-000000000001")] // future format
        public void RejectsAnythingItCannotSafelyAct(string payload)
        {
            Assert.False(RoleHandoff.TryParse(payload, out var parsed));
            Assert.Null(parsed);
        }

        // TargetArgument is dynamic, so a receiver can be handed literally any object.
        [Fact]
        public void RejectsANonStringPayload()
        {
            Assert.False(RoleHandoff.TryParse(new object(), out var parsed));
            Assert.Null(parsed);
        }

        // Pinned as a literal, not as a round trip: a round trip through our own encoder stays
        // green through a format change that would break every Inspector build already out
        // there. The Inspector's own tests parse this same literal from its side of the fence.
        [Fact]
        public void ToPayload_ProducesTheDocumentedWireFormat()
        {
            var payload = new RoleHandoff
            {
                Entity = "systemuser",
                Id = new Guid("11111111-1111-1111-1111-111111111111"),
                Name = "Ada Lovelace",
                BusinessUnitId = new Guid("22222222-2222-2222-2222-222222222222"),
                BusinessUnitName = "Contoso Ltd"
            }.ToPayload();

            Assert.Equal(
                "xtbrolehandoff:v=1&entity=systemuser&id=11111111-1111-1111-1111-111111111111" +
                "&name=Ada%20Lovelace&buid=22222222-2222-2222-2222-222222222222&bu=Contoso%20Ltd",
                payload);
        }

        [Fact]
        public void RejectsAnAllZeroId()
        {
            Assert.False(RoleHandoff.TryParse($"xtbrolehandoff:v=1&entity=team&id={Guid.Empty}", out _));
        }
    }
}
