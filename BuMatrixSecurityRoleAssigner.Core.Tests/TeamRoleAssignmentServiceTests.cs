using System;
using System.Collections.Generic;
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
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams();

            var teamItem = Assert.Single(teams);
            Assert.Equal("Sales Team", teamItem.Name);
            Assert.Equal("Owner", teamItem.TeamType);
        }

        [Fact]
        public void RetrieveTeams_ExcludesAccessTeams()
        {
            // Access teams can't hold security roles, so they shouldn't be offered as selectable
            // targets in the first place - see CLAUDE.md architecture notes.
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            fake.SeedTeam(Guid.NewGuid(), "Support Access Team", RootBuId, "Root BU", "Access");
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams();

            Assert.Single(teams);
            Assert.DoesNotContain(teams, t => t.Name == "Support Access Team");
        }

        [Fact]
        public void RetrieveTeams_ExcludesPowerVirtualAgentTeamsByDefault()
        {
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner", "Used by Power Virtual Agents");
            fake.SeedTeam(Guid.NewGuid(), "Normal Team", RootBuId, "Root BU", "Owner", "Used by a business process");
            fake.SeedTeam(Guid.NewGuid(), "Undescribed Team", RootBuId, "Root BU", "Owner");
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams();

            Assert.Equal(2, teams.Count);
            Assert.DoesNotContain(teams, t => t.Name == "Sales Team");
        }

        [Fact]
        public void RetrieveTeams_IncludesPowerVirtualAgentTeamsWhenIgnoreIsDisabled()
        {
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Agent Team", RootBuId, "Root BU", "Owner", "POWER VIRTUAL AGENTS managed team");
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams(ignorePowerVirtualAgentTeams: false);

            Assert.Contains(teams, t => t.Name == "Agent Team");
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
        public void RetrieveRoles_SortsByNameThenBusinessUnit()
        {
            // Seeded out of order on both axes: role name descending, and within the
            // "Salesperson" name, business unit name descending too - RetrieveRoles must sort
            // by name first, then business unit, regardless of seed/insertion order.
            var fake = new FakeOrganizationService();
            fake.SeedRole(Guid.NewGuid(), "Sales Manager", RootBuId, "Zebra BU");
            var salespersonZebra = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Zebra BU");
            var salespersonAlpha = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Alpha BU");
            var sut = new TeamRoleAssignmentService(fake);

            var roles = sut.RetrieveRoles();

            Assert.Equal(
                new[] { "Sales Manager", "Salesperson", "Salesperson" },
                roles.Select(r => r.Name).ToArray());
            var salespersonRoles = roles.Where(r => r.Name == "Salesperson").ToArray();
            Assert.Equal(new[] { salespersonAlpha.Id, salespersonZebra.Id }, salespersonRoles.Select(r => r.Id).ToArray());
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
        public void AssignOrRemove_ReportsProgress_InRoleUnits_OpeningAtZero_ClosingAtTheTotal()
        {
            // 2 teams x 1 role = 2 role units. Each target boundary reports (see
            // ...ReportsProgress_AtEveryTargetBoundary...), then a closing report that has to land
            // on the total - otherwise the percentage the caller shows never reaches 100.
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Team A", RootBuId, "Root BU", "Owner");
            fake.SeedTeam(Guid.NewGuid(), "Team B", RootBuId, "Root BU", "Owner");
            fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams().OrderBy(t => t.Name).ToList();
            var roles = sut.RetrieveRoles();
            var reported = new List<AssignRemoveProgress>();

            sut.AssignOrRemove(teams, roles, roles, add: true, progress: p => reported.Add(p));

            Assert.Equal(3, reported.Count);
            Assert.Equal(0, reported[0].UnitsDone);
            Assert.Equal(2, reported[0].TotalUnits);
            Assert.Equal(0, reported[0].TargetsDone);
            Assert.Equal(2, reported[0].TotalTargets);
            Assert.Equal("Team A", reported[0].CurrentTargetName);

            Assert.Equal(1, reported[1].UnitsDone);
            Assert.Equal(1, reported[1].TargetsDone);
            Assert.Equal("Team B", reported[1].CurrentTargetName);

            Assert.Equal(2, reported[2].UnitsDone);
            Assert.Equal(2, reported[2].TargetsDone);
        }

        [Fact]
        public void AssignOrRemove_ReportsProgress_AtEveryTargetBoundary_EvenBelowTheThrottle()
        {
            // 4 teams x 1 role is 4 units - fewer than one throttle interval. Throttling target
            // boundaries too would leave the whole run showing 0% with no ETA until it finished,
            // which is worse than the per-target reporting role units replaced.
            var fake = new FakeOrganizationService();
            for (var i = 0; i < 4; i++)
                fake.SeedTeam(Guid.NewGuid(), $"Team {i}", RootBuId, "Root BU", "Owner");
            fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);

            var roles = sut.RetrieveRoles();
            var reported = new List<AssignRemoveProgress>();

            sut.AssignOrRemove(sut.RetrieveTeams(), roles, roles, add: true, progress: p => reported.Add(p));

            Assert.Equal(new[] { 0, 1, 2, 3, 4 }, reported.Select(p => p.UnitsDone));
        }

        [Fact]
        public void AssignOrRemove_ReportsProgress_EveryTenRoleUnits_NotEveryRole()
        {
            // One team x 25 roles: progress has to move within the team (the point of counting
            // role units rather than targets), but no more often than every 10 units, plus the
            // forced closing report at 25.
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            for (var i = 0; i < 25; i++)
                fake.SeedRole(Guid.NewGuid(), $"Role {i:00}", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);

            var teams = sut.RetrieveTeams();
            var roles = sut.RetrieveRoles();
            var reported = new List<AssignRemoveProgress>();

            sut.AssignOrRemove(teams, roles, roles, add: true, progress: p => reported.Add(p));

            Assert.Equal(new[] { 0, 10, 20, 25 }, reported.Select(p => p.UnitsDone));
            Assert.All(reported, p => Assert.Equal(25, p.TotalUnits));
        }

        [Fact]
        public void AssignOrRemove_ProgressTotalUnits_IsTargetsTimesRoles()
        {
            var fake = new FakeOrganizationService();
            for (var i = 0; i < 3; i++)
                fake.SeedTeam(Guid.NewGuid(), $"Team {i}", RootBuId, "Root BU", "Owner");
            for (var i = 0; i < 4; i++)
                fake.SeedRole(Guid.NewGuid(), $"Role {i}", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);

            var roles = sut.RetrieveRoles();
            var reported = new List<AssignRemoveProgress>();

            sut.AssignOrRemove(sut.RetrieveTeams(), roles, roles, add: true, progress: p => reported.Add(p));

            Assert.Equal(12, reported[0].TotalUnits);
            Assert.Equal(12, reported[reported.Count - 1].UnitsDone);
        }

        [Fact]
        public void AssignOrRemove_ProgressTotalUnits_CountsTheRemoveFromAllBusWidening()
        {
            // One role selected, but "remove from all BUs" widens it to both BU copies - so the
            // run is 2 role units, not 1.
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var rootRoleId = Guid.NewGuid();
            var rootRole = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childBuId = Guid.NewGuid();
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);
            fake.SeedTeamRole(team.Id, rootRole.Id);
            fake.SeedTeamRole(team.Id, childCopy.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var allRoles = sut.RetrieveRoles();
            var selected = new[] { allRoles.Single(r => r.Id == rootRole.Id) };
            var reported = new List<AssignRemoveProgress>();

            sut.AssignOrRemove(sut.RetrieveTeams(), selected, allRoles, add: false,
                removeFromAllBus: true, progress: p => reported.Add(p));

            Assert.Equal(2, reported[0].TotalUnits);
            Assert.Equal(2, reported[reported.Count - 1].UnitsDone);
        }

        [Fact]
        public void AssignOrRemove_Progress_ReachesTheTotal_WhenEveryUnitIsSkipped()
        {
            // Nothing to do (all three roles are already assigned), but the bar still has to
            // finish at 100% rather than stalling wherever it last reported.
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            for (var i = 0; i < 3; i++)
                fake.SeedTeamRole(team.Id, fake.SeedRole(Guid.NewGuid(), $"Role {i}", RootBuId, "Root BU").Id);
            var sut = new TeamRoleAssignmentService(fake);

            var roles = sut.RetrieveRoles();
            var reported = new List<AssignRemoveProgress>();

            var log = sut.AssignOrRemove(sut.RetrieveTeams(), roles, roles, add: true, progress: p => reported.Add(p));

            Assert.Equal(0, log.Changed);
            Assert.Equal(3, log.AlreadyPresent.Count);
            Assert.Equal(3, reported[reported.Count - 1].UnitsDone);
            Assert.Equal(3, reported[reported.Count - 1].TotalUnits);
        }

        [Fact]
        public void AssignOrRemove_Progress_ReachesTheTotal_WhenReadingExistingRolesFails()
        {
            // The target is abandoned before any role is attempted; its units still have to be
            // credited or the bar never reaches 100%.
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.SeedRole(Guid.NewGuid(), "Sales Manager", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);
            var teams = sut.RetrieveTeams();
            var roles = sut.RetrieveRoles();

            // Only the "roles currently on this team" read (role joined to teamroles) faults.
            fake.RetrieveMultipleFaultPredicate = q => q.EntityName == "role" && q.LinkEntities.Count > 0;
            var reported = new List<AssignRemoveProgress>();

            var log = sut.AssignOrRemove(teams, roles, roles, add: true, progress: p => reported.Add(p));

            Assert.Single(log.Errors);
            Assert.Equal(2, reported[reported.Count - 1].UnitsDone);
            Assert.Equal(2, reported[reported.Count - 1].TotalUnits);
        }

        [Fact]
        public void AssignOrRemove_SplitsAssociateIntoBatchesOfTen()
        {
            // Progress can only move inside a target if the platform call is chunked - a single
            // Associate for all 25 roles would report nothing until the whole team was done.
            var fake = new FakeOrganizationService();
            fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            for (var i = 0; i < 25; i++)
                fake.SeedRole(Guid.NewGuid(), $"Role {i:00}", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);
            var roles = sut.RetrieveRoles();

            var log = sut.AssignOrRemove(sut.RetrieveTeams(), roles, roles, add: true);

            Assert.Equal(25, log.Changed);
            Assert.Equal(new[] { 10, 10, 5 }, fake.AssociateBatchSizes);
        }

        [Fact]
        public void AssignOrRemove_ClassicBuFallback_SpanningSeveralBatches_WarnsOncePerTarget()
        {
            // 12 roles is two batches and the classic-BU fallback fires in both, but the summary
            // counts teams - so the team must still contribute exactly one warning line.
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var childTeam = fake.SeedTeam(Guid.NewGuid(), "Child BU Team", childBuId, "Child BU", "Owner");
            var rootRoleIds = new HashSet<Guid>();
            for (var i = 0; i < 12; i++)
            {
                var rootRoleId = Guid.NewGuid();
                rootRoleIds.Add(rootRoleId);
                fake.SeedRole(rootRoleId, $"Role {i:00}", RootBuId, "Root BU");
                fake.SeedRole(Guid.NewGuid(), $"Role {i:00}", childBuId, "Child BU", rootRoleId);
            }
            // Classic-BU org: associating a root-BU role onto a child-BU team faults.
            fake.FaultPredicate = (entityName, entityId, relationship, related) =>
                related.Any(r => rootRoleIds.Contains(r.Id));
            var sut = new TeamRoleAssignmentService(fake);

            var allRoles = sut.RetrieveRoles();
            var selected = allRoles.Where(r => rootRoleIds.Contains(r.Id)).ToList();

            var log = sut.AssignOrRemove(sut.RetrieveTeams(), selected, allRoles, add: true);

            Assert.Equal(12, log.Changed);
            Assert.Empty(log.Errors);
            var warning = Assert.Single(log.ClassicBuDetected);
            Assert.Contains("Child BU Team", warning);
            Assert.Contains("12 role(s)", warning);
            Assert.Equal(12, sut.GetTeamRoleIds(childTeam.Id).Count);
        }

        [Fact]
        public void AssignOrRemove_ClassicBuFallback_TwoSelectedCopiesOfOneRole_ResolveToOneRow()
        {
            // Classic-BU org, and the user selected two BU copies of the same logical role. Both
            // resolve onto the one copy that lives in the team's own BU - it has to be queued once,
            // or the retry would (dis)associate a duplicate row and fault on it.
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var childTeam = fake.SeedTeam(Guid.NewGuid(), "Child BU Team", childBuId, "Child BU", "Owner");
            var otherBuId = Guid.NewGuid();

            var rootRoleId = Guid.NewGuid();
            var rootRole = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var otherBuCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", otherBuId, "Other BU", rootRoleId);
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);

            // Neither selected copy lives in the team's BU, so both fault and both resolve to childCopy.
            fake.FaultPredicate = (entityName, entityId, relationship, related) =>
                related.Any(r => r.Id == rootRole.Id || r.Id == otherBuCopy.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var allRoles = sut.RetrieveRoles();
            var selected = allRoles.Where(r => r.Id == rootRole.Id || r.Id == otherBuCopy.Id).ToList();

            var log = sut.AssignOrRemove(sut.RetrieveTeams(), selected, allRoles, add: true);

            Assert.Empty(log.Errors);
            Assert.Equal(1, log.Changed);
            // First call is the faulted pair, then the single deduped retry - not a retry of two.
            Assert.Equal(new[] { 2, 1 }, fake.AssociateBatchSizes);
            Assert.Equal(new[] { childCopy.Id }, sut.GetTeamRoleIds(childTeam.Id));
        }

        [Fact]
        public void AssignOrRemove_TargetThatFaultsEveryBatch_ReportsTheErrorOncePerTarget()
        {
            // All three batches fault with the same message; the summary should read as one
            // broken team, not three identical errors.
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Broken Team", RootBuId, "Root BU", "Owner");
            for (var i = 0; i < 25; i++)
                fake.SeedRole(Guid.NewGuid(), $"Role {i:00}", RootBuId, "Root BU");
            fake.FaultPredicate = (entityName, entityId, relationship, related) => entityId == team.Id;
            var sut = new TeamRoleAssignmentService(fake);
            var roles = sut.RetrieveRoles();

            var log = sut.AssignOrRemove(sut.RetrieveTeams(), roles, roles, add: true);

            Assert.Equal(0, log.Changed);
            var error = Assert.Single(log.Errors);
            Assert.Contains("Broken Team", error);
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
        public void AssignOrRemove_Remove_Default_OnlyRemovesSelectedBuCopy_LeavesOtherCopiesAssigned()
        {
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var rootRoleId = Guid.NewGuid();
            var rootCopy = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childBuId = Guid.NewGuid();
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);
            fake.SeedTeamRole(team.Id, rootCopy.Id);
            fake.SeedTeamRole(team.Id, childCopy.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var teamItem = sut.RetrieveTeams().Single();
            var allRoles = sut.RetrieveRoles();
            var selected = allRoles.Where(r => r.Id == rootCopy.Id).ToList();

            var log = sut.AssignOrRemove(new[] { teamItem }, selected, allRoles, add: false);

            Assert.Equal(1, log.Changed);
            var remaining = sut.GetTeamRoleIds(team.Id);
            Assert.DoesNotContain(rootCopy.Id, remaining);
            Assert.Contains(childCopy.Id, remaining);
        }

        [Fact]
        public void AssignOrRemove_RemoveFromAllBus_RemovesEveryBuCopyPresentOnTeam_AndSkipsAbsentOnes()
        {
            var fake = new FakeOrganizationService();
            var team = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var rootRoleId = Guid.NewGuid();
            var rootCopy = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childBuId = Guid.NewGuid();
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);
            var otherBuId = Guid.NewGuid();
            fake.SeedRole(Guid.NewGuid(), "Salesperson", otherBuId, "Other BU", rootRoleId);
            fake.SeedTeamRole(team.Id, rootCopy.Id);
            fake.SeedTeamRole(team.Id, childCopy.Id);
            // The otherBuId copy is never assigned to the team - should be reported as
            // not-present, not an error.
            var sut = new TeamRoleAssignmentService(fake);

            var teamItem = sut.RetrieveTeams().Single();
            var allRoles = sut.RetrieveRoles();
            var selected = allRoles.Where(r => r.Id == rootCopy.Id).ToList();

            var log = sut.AssignOrRemove(new[] { teamItem }, selected, allRoles, add: false, removeFromAllBus: true);

            Assert.Equal(2, log.Changed);
            Assert.Single(log.NotPresent);
            Assert.Empty(sut.GetTeamRoleIds(team.Id));
        }

        [Fact]
        public void AssignOrRemove_Add_AccessTeamFault_IsCapturedPerTeam_NotFatal()
        {
            // RetrieveTeams now filters access teams out at the query level (see
            // RetrieveTeams_ExcludesAccessTeams), so an access team can no longer reach
            // AssignOrRemove via the normal UI flow. This test still exercises the fallback
            // catch-and-report path directly - kept as a safety net for any other target type
            // that legitimately can't hold a role - by constructing the TeamItem by hand instead
            // of going through RetrieveTeams.
            var fake = new FakeOrganizationService();
            var accessTeam = fake.SeedTeam(Guid.NewGuid(), "Support Access Team", RootBuId, "Root BU", "Access");
            var ownerTeam = fake.SeedTeam(Guid.NewGuid(), "Sales Team", RootBuId, "Root BU", "Owner");
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.FaultPredicate = (entityName, entityId, relationship, related) => entityId == accessTeam.Id;
            var sut = new TeamRoleAssignmentService(fake);

            var teams = new[]
            {
                new TeamItem { Id = accessTeam.Id, Name = "Support Access Team", BusinessUnitId = RootBuId, BusinessUnitName = "Root BU", TeamType = "Access" },
                new TeamItem { Id = ownerTeam.Id, Name = "Sales Team", BusinessUnitId = RootBuId, BusinessUnitName = "Root BU", TeamType = "Owner" },
            };
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
        public void RetrieveUsers_ReturnsSeededUsers_WithDisabledFlag()
        {
            var fake = new FakeOrganizationService();
            fake.SeedUser(Guid.NewGuid(), "Alice Active", RootBuId, "Root BU");
            fake.SeedUser(Guid.NewGuid(), "Bob Disabled", RootBuId, "Root BU", isDisabled: true);
            var sut = new TeamRoleAssignmentService(fake);

            var users = sut.RetrieveUsers();

            Assert.Equal(2, users.Count);
            Assert.Contains(users, u => u.Name == "Alice Active" && !u.IsDisabled);
            Assert.Contains(users, u => u.Name == "Bob Disabled" && u.IsDisabled);
        }

        [Fact]
        public void AssignOrRemove_Users_AssignsExactRoleAndBu_AndSkipsAlreadyAssigned()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Alice", RootBuId, "Root BU");
            var newRole = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var alreadyAssignedRole = fake.SeedRole(Guid.NewGuid(), "Sales Manager", RootBuId, "Root BU");
            fake.SeedUserRole(user.Id, alreadyAssignedRole.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var userItem = sut.RetrieveUsers().Single();
            var roles = sut.RetrieveRoles();
            var selected = roles.Where(r => r.Id == newRole.Id || r.Id == alreadyAssignedRole.Id).ToList();

            var log = sut.AssignOrRemove(new[] { userItem }, selected, roles, add: true);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.AlreadyPresent);
            Assert.Empty(log.Errors);
            var userRoles = sut.GetUserRoleIds(user.Id);
            Assert.Contains(newRole.Id, userRoles);
            Assert.Contains(alreadyAssignedRole.Id, userRoles);
        }

        [Fact]
        public void AssignOrRemove_Users_Remove_RemovesAssignedRole_AndSkipsNotAssigned()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Alice", RootBuId, "Root BU");
            var assignedRole = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var neverAssignedRole = fake.SeedRole(Guid.NewGuid(), "Sales Manager", RootBuId, "Root BU");
            fake.SeedUserRole(user.Id, assignedRole.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var userItem = sut.RetrieveUsers().Single();
            var roles = sut.RetrieveRoles();
            var selected = roles.Where(r => r.Id == assignedRole.Id || r.Id == neverAssignedRole.Id).ToList();

            var log = sut.AssignOrRemove(new[] { userItem }, selected, roles, add: false);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.NotPresent);
            Assert.Empty(log.Errors);
            Assert.DoesNotContain(assignedRole.Id, sut.GetUserRoleIds(user.Id));
        }

        [Fact]
        public void AssignOrRemove_Add_DisabledUser_StillAssigns_ButWarns()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Bob Disabled", RootBuId, "Root BU", isDisabled: true);
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            var sut = new TeamRoleAssignmentService(fake);

            var userItem = sut.RetrieveUsers().Single();
            var roles = sut.RetrieveRoles();

            var log = sut.AssignOrRemove(new[] { userItem }, roles, roles, add: true);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.DisabledUserWarnings);
            Assert.Contains("Bob Disabled", log.DisabledUserWarnings[0]);
            Assert.Contains(role.Id, sut.GetUserRoleIds(user.Id));
        }

        [Fact]
        public void AssignOrRemove_Remove_DisabledUser_DoesNotWarn()
        {
            var fake = new FakeOrganizationService();
            var user = fake.SeedUser(Guid.NewGuid(), "Bob Disabled", RootBuId, "Root BU", isDisabled: true);
            var role = fake.SeedRole(Guid.NewGuid(), "Salesperson", RootBuId, "Root BU");
            fake.SeedUserRole(user.Id, role.Id);
            var sut = new TeamRoleAssignmentService(fake);

            var userItem = sut.RetrieveUsers().Single();
            var roles = sut.RetrieveRoles();

            var log = sut.AssignOrRemove(new[] { userItem }, roles, roles, add: false);

            Assert.Equal(1, log.Changed);
            Assert.Empty(log.DisabledUserWarnings);
        }

        [Fact]
        public void AssignOrRemove_Users_ProbeFaults_FallsBackToUsersBuCopy_AndWarns()
        {
            // Same classic-BU behavioral probe as the team path, exercised for a user target -
            // proves the shared AssignOrRemove/AssociateOrDisassociate logic isn't team-only.
            var fake = new FakeOrganizationService();
            var childBuId = Guid.NewGuid();
            var user = fake.SeedUser(Guid.NewGuid(), "Alice", childBuId, "Child BU");

            var rootRoleId = Guid.NewGuid();
            var rootRole = fake.SeedRole(rootRoleId, "Salesperson", RootBuId, "Root BU");
            var childCopy = fake.SeedRole(Guid.NewGuid(), "Salesperson", childBuId, "Child BU", rootRoleId);

            fake.FaultPredicate = (entityName, entityId, relationship, related) =>
                related.Any(r => r.Id == rootRoleId);

            var sut = new TeamRoleAssignmentService(fake);
            var userItem = sut.RetrieveUsers().Single();
            var allRoles = sut.RetrieveRoles();
            var selected = new[] { allRoles.Single(r => r.Id == rootRole.Id) };

            var log = sut.AssignOrRemove(new[] { userItem }, selected, allRoles, add: true);

            Assert.Equal(1, log.Changed);
            Assert.Single(log.ClassicBuDetected);
            Assert.Contains(childCopy.Id, sut.GetUserRoleIds(user.Id));
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
