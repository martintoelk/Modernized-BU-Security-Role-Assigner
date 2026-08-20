using System;
using System.Linq;
using Xunit;

namespace BuMatrixSecurityRoleAssigner.Core.Tests
{
    public class TeamRoleAssignmentServiceTests
    {
        private static readonly Guid RootBuId = Guid.NewGuid();

        [Fact]
        public void RetrieveTeams_ReturnsSeededTeams()
        {
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            fake.SeedTeam(Guid.NewGuid(), "Support Access Team", RootBuId, "Root BU", "Access");
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams();

            Assert.Equal(2, teams.Count);
            Assert.Contains(teams, t => t.Name == "Sales Team" && t.TeamType == "Owner");
            Assert.Contains(teams, t => t.Name == "Support Access Team" && t.TeamType == "Access");
        }

        [Fact]
        public void RetrieveRoles_FallsBackRootRoleIdToOwnId_WhenNoParentRoot()
        {
            var fake = new FakeOrganizationService();
            var roleId = Guid.NewGuid();
            fake.SeedRole(roleId, "Salesperson", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);

            var role = Assert.Single(sut.RetrieveRoles());

            Assert.Equal(roleId, role.RootRoleId);
        }

        [Fact]
        public void RetrieveRoles_UsesParentRootRoleId_WhenBuCopy()
        {
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var rootRoleId = Guid.NewGuid();
            fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var buCopyId = Guid.NewGuid();
            fake.SeedRole(buCopyId, "Salesperson", childBuId, "Child BU", rootRoleId);
            var sut = new TeamRoleAssignmentService(fake);

            var buCopy = sut.RetrieveRoles().Single(r => r.Id == buCopyId);

            Assert.Equal(rootRoleId, buCopy.RootRoleId);
        }

        [Fact]
        public void AssignOrRemove_Add_AssignsExactRoleAndBu_AndSkipsAlreadyAssigned()
        {
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var newRole = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var alreadyAssignedRole = fake.SeedRole(Guid.NewGuid(), "Sales Manager", RootBuId, "Root BU");
            fake.SeedTeamRole(team.Id, alreadyAssignedRole.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var teamItem = sut.RetrieveTeams().Single();
            var roles = sut.RetrieveRoles();
            var selected = roles.Where(r => r.Id == newRole.Id || r.Id == alreadyAssignedRole.Id).ToList();

            var log = sut.AssignOrRemove(new[] { teamItem }, selected, roles, add: true, matchBu: false);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.AlreadyPresent);
            Assert.Empty(log.Errors);
            var teamRoles = sut.GetTeamRoleIds(team.Id);
            Assert.Contains(newRole.Id, teamRoles);
            Assert.Contains(alreadyAssignedRole.Id, teamRoles);
        }

        [Fact]
        public void AssignOrRemove_Remove_RemovesAssignedRole_AndSkipsNotAssigned()
        {
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var assignedRole = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var neverAssignedRole = fake.SeedRole(Guid.NewGuid(), "Sales Manager", RootBuId, "Root BU");
            fake.SeedTeamRole(team.Id, assignedRole.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var teamItem = sut.RetrieveTeams().Single();
            var roles = sut.RetrieveRoles();
            var selected = roles.Where(r => r.Id == assignedRole.Id || r.Id == neverAssignedRole.Id).ToList();

            var log = sut.AssignOrRemove(new[] { teamItem }, selected, roles, add: false, matchBu: false);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.NotPresent);
            Assert.Empty(log.Errors);
            Assert.DoesNotContain(assignedRole.Id, sut.GetTeamRoleIds(team.Id));
        }

        [Fact]
        public void AssignOrRemove_Add_AccessTeamFault_IsCapturedPerTeam_NotFatal()
        {
            var fake = new FakeOrganizationService();
            var accessTeam = fake.SeedTeam(Guid.NewGuid(), "Support Access Team", RootBuId, "Root BU", "Access");
            var ownerTeam = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.FaultPredicate = (entityName, entityId, relationship) => entityId == accessTeam.Id;
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams();
            var roles = sut.RetrieveRoles();

            var log = sut.AssignOrRemove(teams, roles, roles, add: true, matchBu: false);

            Assert.Equal(1, log.Changed); // only the owner team's assignment went through
            Assert.Single(log.Errors);
            Assert.Contains("Support Access Team", log.Errors[0]);
            Assert.Contains(role.Id, sut.GetTeamRoleIds(ownerTeam.Id));
            Assert.DoesNotContain(role.Id, sut.GetTeamRoleIds(accessTeam.Id));
        }

        [Fact]
        public void AssignOrRemove_MatchBu_ResolvesToTeamsBuCopy_AndSkipsTeamsWithNoCopy()
        {
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var childTeam = fake.SeedTeam(Guid.NewGuid(), "Child BU Team", childBuId, "Child BU", "Owner");
            var otherBuId = Guid.NewGuid();
            var otherTeam = fake.SeedTeam(Guid.NewGuid(), "Other BU Team", otherBuId, "Other BU", "Owner");

            var rootRoleId = Guid.NewGuid();
            var rootRole = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);
            // no copy seeded for otherBuId

            var sut = new TeamRoleAssignmentService(fake);
            var teams = sut.RetrieveTeams();
            var allRoles = sut.RetrieveRoles();
            var selected = new[] { allRoles.Single(r => r.Id == rootRole.Id) };

            var log = sut.AssignOrRemove(teams, selected, allRoles, add: true, matchBu: true);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.NoRoleInBu);
            Assert.Contains(childCopy.Id, sut.GetTeamRoleIds(childTeam.Id));
            Assert.Empty(sut.GetTeamRoleIds(otherTeam.Id));
        }
    }
}
