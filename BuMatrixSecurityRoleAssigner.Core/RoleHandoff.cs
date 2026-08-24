using System;
using System.Collections.Generic;
using System.Text;

namespace BuMatrixSecurityRoleAssigner.Core
{
    /// <summary>
    /// The payload this tool hands to "User/Team Role Inspector" over XrmToolBox's message bus
    /// (issue #17): "here is the team or user I have selected - show me its roles".
    /// <para>
    /// <b>This is a wire format, not an object contract.</b> The two tools ship as separate
    /// plugin assemblies from separate repos and never reference each other, so a
    /// <c>MessageBusEventArgs.TargetArgument</c> carrying an instance of this class would arrive
    /// at the Inspector as a type it cannot name - only reflection would get at it, and only for
    /// as long as the property names on both sides happened to agree. So the thing that actually
    /// crosses the boundary is <see cref="ToPayload"/>'s string, which every .NET version of both
    /// tools can read. (FetchXML Builder's published integration contract is a bare string for
    /// the same reason.) The Inspector carries its own copy of this parser.
    /// </para>
    /// <para>
    /// Format: <c>xtbrolehandoff:v=1&amp;entity=team&amp;id=&lt;guid&gt;&amp;name=&lt;name&gt;</c>,
    /// optionally <c>&amp;buid=&lt;guid&gt;&amp;bu=&lt;name&gt;</c>. Values are escaped with
    /// <see cref="Uri.EscapeDataString"/>, since Dataverse names may contain any of the
    /// separators. Keys may be added while <c>v</c> stays 1 - a receiver ignores keys it does not
    /// know - so <c>v</c> only moves when the meaning of an existing key changes, and a receiver
    /// refuses a version it was not built for rather than guessing.
    /// </para>
    /// </summary>
    public class RoleHandoff
    {
        private const string Prefix = "xtbrolehandoff:";
        private const string Version = "1";

        /// <summary>Logical name of the record: <c>team</c> or <c>systemuser</c>.</summary>
        public string Entity { get; set; }

        /// <summary>Id of the team/user to inspect. The receiver re-resolves everything else from it.</summary>
        public Guid Id { get; set; }

        /// <summary>Display name, so the receiver can say what it is opening before it has resolved <see cref="Id"/>.</summary>
        public string Name { get; set; }

        /// <summary>Owning business unit, when known. Context only - never needed to resolve <see cref="Id"/>.</summary>
        public Guid? BusinessUnitId { get; set; }

        /// <summary>Owning business unit's name, when known.</summary>
        public string BusinessUnitName { get; set; }

        /// <summary>Builds the handoff for a selected row in the target grid.</summary>
        public static RoleHandoff ForTarget(IAssignmentTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            return new RoleHandoff
            {
                // team / systemuser - the same logical names the assignment path already uses,
                // so the two tools agree on the vocabulary without a second mapping to keep.
                Entity = target.EntityLogicalName,
                Id = target.Id,
                Name = target.Name,
                BusinessUnitId = target.BusinessUnitId == Guid.Empty ? (Guid?)null : target.BusinessUnitId,
                BusinessUnitName = target.BusinessUnitName
            };
        }

        /// <summary>Encodes this handoff as the string to put on <c>MessageBusEventArgs.TargetArgument</c>.</summary>
        public string ToPayload()
        {
            var sb = new StringBuilder(Prefix);
            sb.Append("v=").Append(Version);
            sb.Append("&entity=").Append(Escape(Entity));
            sb.Append("&id=").Append(Id.ToString("D"));
            sb.Append("&name=").Append(Escape(Name));
            if (BusinessUnitId.HasValue)
                sb.Append("&buid=").Append(BusinessUnitId.Value.ToString("D"));
            if (!string.IsNullOrEmpty(BusinessUnitName))
                sb.Append("&bu=").Append(Escape(BusinessUnitName));
            return sb.ToString();
        }

        /// <summary>
        /// Reads a handoff off a <c>MessageBusEventArgs.TargetArgument</c>. That argument is
        /// <c>dynamic</c> and the sender may be any tool, so this takes <see cref="object"/> and
        /// answers false - rather than throwing - for anything that is not one of ours. A
        /// receiver that cannot act on a message should ignore it, not fail in front of the user.
        /// </summary>
        public static bool TryParse(object payload, out RoleHandoff handoff)
        {
            handoff = null;

            if (!(payload is string text)) return false;
            if (!text.StartsWith(Prefix, StringComparison.Ordinal)) return false;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in text.Substring(Prefix.Length).Split('&'))
            {
                if (pair.Length == 0) return false;
                var split = pair.IndexOf('=');
                if (split <= 0) return false;
                var key = pair.Substring(0, split);
                if (values.ContainsKey(key)) return false;
                values[key] = pair.Substring(split + 1);
            }

            if (!values.TryGetValue("v", out var version) || version != Version) return false;
            if (!values.TryGetValue("entity", out var rawEntity) ||
                !TryUnescape(rawEntity, out var entity) ||
                (entity != "team" && entity != "systemuser")) return false;
            if (!values.TryGetValue("id", out var rawId)) return false;
            if (!Guid.TryParse(rawId, out var id) || id == Guid.Empty) return false;

            Guid? businessUnitId = null;
            if (values.TryGetValue("buid", out var rawBuId))
            {
                if (!Guid.TryParse(rawBuId, out var buId) || buId == Guid.Empty) return false;
                businessUnitId = buId;
            }

            string name = null;
            if (values.TryGetValue("name", out var rawName) && !TryUnescape(rawName, out name))
                return false;

            string businessUnitName = null;
            if (values.TryGetValue("bu", out var rawBusinessUnitName) &&
                !TryUnescape(rawBusinessUnitName, out businessUnitName))
                return false;

            handoff = new RoleHandoff
            {
                Entity = entity,
                Id = id,
                Name = name,
                BusinessUnitId = businessUnitId,
                BusinessUnitName = businessUnitName
            };
            return true;
        }

        private static string Escape(string value) =>
            string.IsNullOrEmpty(value) ? string.Empty : Uri.EscapeDataString(value);

        private static bool TryUnescape(string value, out string result)
        {
            // UnescapeDataString is permissive about malformed percent sequences on some .NET
            // Framework versions. Validate the wire encoding first so a malformed message is
            // rejected consistently rather than being treated as a different valid value.
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] != '%') continue;
                if (i + 2 >= value.Length || !IsHex(value[i + 1]) || !IsHex(value[i + 2]))
                {
                    result = null;
                    return false;
                }
                i += 2;
            }

            try
            {
                result = Uri.UnescapeDataString(value);
                return true;
            }
            catch (UriFormatException)
            {
                result = null;
                return false;
            }
        }

        private static bool IsHex(char value) =>
            (value >= '0' && value <= '9') ||
            (value >= 'a' && value <= 'f') ||
            (value >= 'A' && value <= 'F');
    }
}
