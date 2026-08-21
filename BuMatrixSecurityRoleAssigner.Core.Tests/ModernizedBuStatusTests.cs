using System;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace BuMatrixSecurityRoleAssigner.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="TeamRoleAssignmentService.GetModernizedBuStatus"/> - the informational
    /// read of the undocumented <c>EnableOwnershipAcrossBusinessUnits</c> OrgDBOrgSetting from
    /// <c>organization.orgdborgsettings</c>. See issue #15 and docs/research/modernized-vs-classic-bu-detection.md.
    /// </summary>
    public class ModernizedBuStatusTests
    {
        private static Entity SeedOrganization(FakeOrganizationService fake, string orgDbOrgSettingsXml)
        {
            var org = new Entity("organization", Guid.NewGuid());
            if (orgDbOrgSettingsXml != null)
                org["orgdborgsettings"] = orgDbOrgSettingsXml;
            return fake.Seed(org);
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsYes_WhenElementIsTrue()
        {
            var fake = new FakeOrganizationService();
            SeedOrganization(fake, "<OrgSettings><EnableOwnershipAcrossBusinessUnits>true</EnableOwnershipAcrossBusinessUnits></OrgSettings>");
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.Yes, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsYes_WhenElementIsOne()
        {
            var fake = new FakeOrganizationService();
            SeedOrganization(fake, "<OrgSettings><EnableOwnershipAcrossBusinessUnits>1</EnableOwnershipAcrossBusinessUnits></OrgSettings>");
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.Yes, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsYes_WhenRootElementHasDefaultNamespace()
        {
            var fake = new FakeOrganizationService();
            SeedOrganization(fake,
                "<OrgSettings xmlns=\"http://schemas.microsoft.com/xrm/2011/OrgSettings\">" +
                "<EnableOwnershipAcrossBusinessUnits>true</EnableOwnershipAcrossBusinessUnits></OrgSettings>");
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.Yes, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsNo_WhenElementIsFalse()
        {
            var fake = new FakeOrganizationService();
            SeedOrganization(fake, "<OrgSettings><EnableOwnershipAcrossBusinessUnits>false</EnableOwnershipAcrossBusinessUnits></OrgSettings>");
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.No, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsNo_WhenElementAbsentFromOtherwiseValidXml()
        {
            var fake = new FakeOrganizationService();
            SeedOrganization(fake,
                "<OrgSettings><IsRetentionEnabled>true</IsRetentionEnabled><IsArchivalEnabled>true</IsArchivalEnabled></OrgSettings>");
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.No, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsNo_WhenOrgdborgsettingsIsNullOrEmpty()
        {
            var fake = new FakeOrganizationService();
            SeedOrganization(fake, null);
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.No, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsUnknown_WhenNoOrganizationRowExists()
        {
            var fake = new FakeOrganizationService();
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.Unknown, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsUnknown_WhenXmlIsMalformed()
        {
            var fake = new FakeOrganizationService();
            SeedOrganization(fake, "<OrgSettings><EnableOwnershipAcrossBusinessUnits>true</Enable");
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.Unknown, sut.GetModernizedBuStatus());
        }

        [Fact]
        public void GetModernizedBuStatus_ReturnsUnknown_WhenReadFaults()
        {
            var fake = new FakeOrganizationService
            {
                RetrieveMultipleFaultPredicate = q => q.EntityName == "organization"
            };
            var sut = new TeamRoleAssignmentService(fake);

            Assert.Equal(ModernizedBuStatus.Unknown, sut.GetModernizedBuStatus());
        }
    }
}
