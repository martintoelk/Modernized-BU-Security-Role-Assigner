using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using BuMatrixSecurityRoleAssigner.Core.Entities;

namespace BuMatrixSecurityRoleAssigner.Core
{
    /// <summary>A team row shown in the left list.</summary>
    public class TeamItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        public string TeamType { get; set; }   // formatted label: Owner / Access / AAD Security Group / AAD Office Group
    }

    /// <summary>A security role row shown in the right list.</summary>
    public class RoleItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }

        /// <summary>
        /// Id of the role in the ROOT business unit. All BU copies of the same logical
        /// role share this value (via parentrootroleid), so it is the key we use to find
        /// "the same role, but in the team's BU".
        /// </summary>
        public Guid RootRoleId { get; set; }

        public EntityReference ToRef() => new EntityReference(Role.EntityLogicalName, Id);
    }

    /// <summary>Collects what happened during an add/remove run so we can show a summary.</summary>
    public class OperationLog
    {
        public int Changed;
        public readonly List<string> AlreadyPresent = new List<string>();
        public readonly List<string> NotPresent = new List<string>();
        public readonly List<string> NoRoleInBu = new List<string>();
        public readonly List<string> Errors = new List<string>();

        /// <summary>
        /// Populated when the exact-role (modernized) association faulted for a team and the
        /// same-BU fallback then succeeded - i.e. auto-detected classic-BU behavior. Surfaced as
        /// a warning rather than a silent behavior switch.
        /// </summary>
        public readonly List<string> ClassicBuDetected = new List<string>();

        public string Summary(bool add)
        {
            var sb = new StringBuilder();
            sb.AppendLine(add
                ? $"Assigned {Changed} role assignment(s)."
                : $"Removed {Changed} role assignment(s).");

            if (add && AlreadyPresent.Count > 0)
                sb.AppendLine($"Skipped {AlreadyPresent.Count} already assigned.");
            if (!add && NotPresent.Count > 0)
                sb.AppendLine($"Skipped {NotPresent.Count} that were not assigned.");
            if (NoRoleInBu.Count > 0)
                sb.AppendLine($"Skipped {NoRoleInBu.Count} with no matching role copy in the team's business unit.");

            if (ClassicBuDetected.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"WARNING: classic business-unit model detected for {ClassicBuDetected.Count} team(s) - " +
                               "roles were matched to each team's own business unit instead of the exact role selected:");
                foreach (var w in ClassicBuDetected.Take(15))
                    sb.AppendLine("  - " + w);
                if (ClassicBuDetected.Count > 15)
                    sb.AppendLine($"  ...and {ClassicBuDetected.Count - 15} more.");
            }

            if (Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{Errors.Count} error(s):");
                foreach (var e in Errors.Take(15))
                    sb.AppendLine("  - " + e);
                if (Errors.Count > 15)
                    sb.AppendLine($"  ...and {Errors.Count - 15} more.");
            }

            return sb.ToString();
        }
    }
}
