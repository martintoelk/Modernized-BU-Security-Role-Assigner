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

            var log = sut.AssignOrRemove(new[] { teamItem }, selected, roles, add: true);

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

            var log = sut.AssignOrRemove(new[] { teamItem }, selected, roles, add: false);

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
            fake.FaultPredicate = (entityName, entityId, relationship, related) => entityId == accessTeam.Id;
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams();
            var roles = sut.RetrieveRoles();

            var log = sut.AssignOrRemove(teams, roles, roles, add: true);

            Assert.Equal(1, log.Changed); // only the owner team's assignment went through
            Assert.Single(log.Errors);
            Assert.Contains("Support Access Team", log.Errors[0]);
            Assert.Contains(role.Id, sut.GetTeamRoleIds(ownerTeam.Id));
            Assert.DoesNotContain(role.Id, sut.GetTeamRoleIds(accessTeam.Id));
        }

        [Fact]
        public void AssignOrRemove_ProbeSucceeds_AssignsExactRole_NoClassicBuWarning()
        {
            // Modernized org: the exact-role Associate just works, so no probe/fallback is needed.
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var otherBuId = Guid.NewGuid();
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", otherBuId, "Other BU");
            var sut = new TeamRoleAssignmentService(fake);

            var teamItem = sut.RetrieveTeams().Single();
            var roles = sut.RetrieveRoles();

            var log = sut.AssignOrRemove(new[] { teamItem }, roles, roles, add: true);

            Assert.Equal(1, log.Changed);
            Assert.Empty(log.ClassicBuDetected);
            Assert.Contains(role.Id, sut.GetTeamRoleIds(team.Id));
        }

        [Fact]
        public void AssignOrRemove_ProbeFaults_FallsBackToTeamsBuCopy_AndWarns()
        {
            // Classic-BU org: the exact-role (cross-BU) Associate faults; the same-BU copy
            // then succeeds on retry, and the service reports it as a detected classic-BU team
            // rather than silently switching or hard-failing.
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var childTeam = fake.SeedTeam(Guid.NewGuid(), "Child BU Team", childBuId, "Child BU", "Owner");
            var otherBuId = Guid.NewGuid();
            var otherTeam = fake.SeedTeam(Guid.NewGuid(), "Other BU Team", otherBuId, "Other BU", "Owner");

            var rootRoleId = Guid.NewGuid();
            var rootRole = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);
            // no copy seeded for otherBuId

            // Simulate a classic-BU org: any cross-BU association (the root role onto a
            // different-BU team) faults; same-BU associations succeed.
            fake.FaultPredicate = (entityName, entityId, relationship, related) =>
                related.Any(r => r.Id == rootRoleId);

            var sut = new TeamRoleAssignmentService(fake);
            var teams = sut.RetrieveTeams();
            var allRoles = sut.RetrieveRoles();
            var selected = new[] { allRoles.Single(r => r.Id == rootRole.Id) };

            var log = sut.AssignOrRemove(teams, selected, allRoles, add: true);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.ClassicBuDetected);
            Assert.Contains("Child BU Team", log.ClassicBuDetected[0]);
            Assert.Single(log.NoRoleInBu); // otherTeam has no BU copy to fall back to
            Assert.Contains(childCopy.Id, sut.GetTeamRoleIds(childTeam.Id));
            Assert.Empty(sut.GetTeamRoleIds(otherTeam.Id));
        }

        [Fact]
        public void AssignOrRemove_MixedBatch_SameBuRoleFaultsAlongsideResolvableRole_StillAssignedOnRetry()
        {
            // A team selects two roles in one call: one it already has direct access to (its own-BU
            // copy) and one that needs classic-BU resolution to a different-BU copy. The single
            // Associate batch faults because of the second role; the first role must not be treated
            // as collateral damage from that fault - it should be retried alone and succeed.
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var childTeam = fake.SeedTeam(Guid.NewGuid(), "Child BU Team", childBuId, "Child BU", "Owner");

            var ownBuRole = fake.SeedRole(Guid.NewGuid(), "Marketing", childBuId, "Child BU");

            var rootRoleId = Guid.NewGuid();
            var rootRole = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);

            // Only the cross-BU root role faults; the team's own-BU role would succeed on its own.
            fake.FaultPredicate = (entityName, entityId, relationship, related) =>
                related.Any(r => r.Id == rootRoleId);

            var sut = new TeamRoleAssignmentService(fake);
            var teams = sut.RetrieveTeams();
            var allRoles = sut.RetrieveRoles();
            var selected = new[]
            {
                allRoles.Single(r => r.Id == ownBuRole.Id),
                allRoles.Single(r => r.Id == rootRole.Id),
            };

            var log = sut.AssignOrRemove(teams, selected, allRoles, add: true);

            Assert.Equal(2, log.Changed);
            Assert.Empty(log.Errors);
            Assert.Single(log.ClassicBuDetected);
            Assert.Contains(ownBuRole.Id, sut.GetTeamRoleIds(childTeam.Id));
            Assert.Contains(childCopy.Id, sut.GetTeamRoleIds(childTeam.Id));
        }

        [Fact]
        public void AssignOrRemove_ProbeFaults_ReRun_IsIdempotent_NoErrorOnSecondPass()
        {
            // Same classic-BU scenario as above, run twice: the second run must not error even
            // though the exact-role Associate faults again - the fallback's BU copy is already
            // assigned by then, so it should be reported as AlreadyPresent, not retried/faulted.
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var childTeam = fake.SeedTeam(Guid.NewGuid(), "Child BU Team", childBuId, "Child BU", "Owner");

            var rootRoleId = Guid.NewGuid();
            var rootRole = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);

            fake.FaultPredicate = (entityName, entityId, relationship, related) =>
                related.Any(r => r.Id == rootRoleId);

            var sut = new TeamRoleAssignmentService(fake);
            var teams = sut.RetrieveTeams();
            var allRoles = sut.RetrieveRoles();
            var selected = new[] { allRoles.Single(r => r.Id == rootRole.Id) };

            var firstRun = sut.AssignOrRemove(teams, selected, allRoles, add: true);
            Assert.Equal(1, firstRun.Changed);
            Assert.Empty(firstRun.Errors);

            var secondRun = sut.AssignOrRemove(teams, selected, allRoles, add: true);

            Assert.Equal(0, secondRun.Changed);
            Assert.Empty(secondRun.Errors);
            Assert.Single(secondRun.AlreadyPresent);
            Assert.Contains(childCopy.Id, sut.GetTeamRoleIds(childTeam.Id));
        }
    }
}
