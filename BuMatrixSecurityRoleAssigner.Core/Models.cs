using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xrm.Sdk;
using BuMatrixSecurityRoleAssigner.Core.Entities;

namespace BuMatrixSecurityRoleAssigner.Core
{
    /// <summary>
    /// A row that can hold security-role assignments - a team or a user. AssignOrRemove works
    /// against either, dispatching the entity/relationship names from the instance rather than
    /// having separate team/user code paths.
    /// </summary>
    public interface IAssignmentTarget
    {
        Guid Id { get; }
        string Name { get; }
        Guid BusinessUnitId { get; }
        string BusinessUnitName { get; }
        string EntityLogicalName { get; }
        string RelationshipSchemaName { get; }
    }

    /// <summary>A team row shown in the target list.</summary>
    public class TeamItem : IAssignmentTarget
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        public string TeamType { get; set; }   // formatted label: Owner / Access / AAD Security Group / AAD Office Group

        public string EntityLogicalName => Entities.Team.EntityLogicalName;
        public string RelationshipSchemaName => Entities.Team.Fields.teamroles_association;
    }

    /// <summary>A user row shown in the target list, in Users mode.</summary>
    public class UserItem : IAssignmentTarget
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public Guid BusinessUnitId { get; set; }
        public string BusinessUnitName { get; set; }
        public bool IsDisabled { get; set; }

        public string EntityLogicalName => Entities.SystemUser.EntityLogicalName;
        public string RelationshipSchemaName => Entities.SystemUser.Fields.systemuserroles_association;
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

    /// <summary>
    /// Whether the connected org has modernized (matrix) business units switched on, per the
    /// undocumented <c>EnableOwnershipAcrossBusinessUnits</c> OrgDBOrgSetting. Informational only
    /// - see <see cref="TeamRoleAssignmentService.GetModernizedBuStatus"/>. Never used to gate
    /// add/remove behavior; the behavioral probe in <see cref="TeamRoleAssignmentService.AssignOrRemove"/>
    /// stays authoritative for that.
    /// </summary>
    public enum ModernizedBuStatus
    {
        /// <summary>The read failed (privilege, throttling, or an unparsable blob) or returned no organization row.</summary>
        Unknown,
        /// <summary><c>EnableOwnershipAcrossBusinessUnits</c> is absent or not "true".</summary>
        No,
        /// <summary><c>EnableOwnershipAcrossBusinessUnits</c> is present and "true".</summary>
        Yes
    }

    /// <summary>
    /// Reported by <see cref="TeamRoleAssignmentService.AssignOrRemove"/> as a run proceeds, so
    /// the caller (background thread) can show overall progress and estimate time remaining
    /// without this layer knowing anything about wall-clock time or UI - it just reports counts.
    /// <para>
    /// Progress is counted in <em>role units</em> - one per (target, role) pair the run will
    /// attempt, after the "remove from all BUs" widening - rather than in targets. Counting
    /// targets makes the bar jump in whole-target steps: 3 teams x 40 roles would only ever read
    /// 0%, 33%, 66%, and no ETA at all until the first team finished. Role units move the bar
    /// (and the ETA derived from it) inside a target too.
    /// </para>
    /// <para>
    /// Within a target, reports are throttled to roughly one per
    /// <see cref="TeamRoleAssignmentService.RoleBatchSize"/> units. Every target boundary reports
    /// regardless, so a run of one role across many targets still moves; the opening report is at
    /// 0 and the closing one always lands on <see cref="TotalUnits"/>, so a caller showing a
    /// percentage both starts at 0% and finishes at 100%.
    /// </para>
    /// </summary>
    public readonly struct AssignRemoveProgress
    {
        public AssignRemoveProgress(int unitsDone, int totalUnits, int targetsDone, int totalTargets, string currentTargetName)
        {
            UnitsDone = unitsDone;
            TotalUnits = totalUnits;
            TargetsDone = targetsDone;
            TotalTargets = totalTargets;
            CurrentTargetName = currentTargetName;
        }

        /// <summary>
        /// Role units settled so far - (dis)associated, or skipped because they were already in
        /// the wanted state. Skips count too, so this always ends on <see cref="TotalUnits"/>.
        /// </summary>
        public int UnitsDone { get; }

        /// <summary>Total role units this run will settle: targets x (widened) selected roles.</summary>
        public int TotalUnits { get; }

        /// <summary>
        /// Number of targets fully processed before the current one (0-based progress) - so the
        /// target being worked on is the (TargetsDone + 1)th. The one exception is the closing
        /// report, which fires after the last target finished and therefore carries
        /// TargetsDone == <see cref="TotalTargets"/>; clamp before rendering "target N of M".
        /// </summary>
        public int TargetsDone { get; }

        /// <summary>Total number of targets in this run.</summary>
        public int TotalTargets { get; }

        /// <summary>
        /// Name of the target now being processed (the (TargetsDone + 1)th of TotalTargets); on
        /// the closing report, the last target of the run.
        /// </summary>
        public string CurrentTargetName { get; }
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

        /// <summary>
        /// Populated (add only) when a role was assigned to a disabled user - not blocked, since
        /// the user explicitly selected them, but surfaced so it's never a silent side effect.
        /// </summary>
        public readonly List<string> DisabledUserWarnings = new List<string>();

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

            if (DisabledUserWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"WARNING: {DisabledUserWarnings.Count} role assignment(s) went to a disabled user:");
                foreach (var w in DisabledUserWarnings.Take(15))
                    sb.AppendLine("  - " + w);
                if (DisabledUserWarnings.Count > 15)
                    sb.AppendLine($"  ...and {DisabledUserWarnings.Count - 15} more.");
            }

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
