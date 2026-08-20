using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace BuMatrixSecurityRoleAssigner.Core.Tests
{
    /// <summary>
    /// Hand-rolled in-memory IOrganizationService double. Supports just enough of the SDK
    /// surface that TeamRoleAssignmentService exercises: RetrieveMultiple over a single base
    /// entity with an optional single-level LinkEntity join and Equal-only criteria, paging via
    /// PageInfo.Count/PageNumber, and Associate/Disassociate against N:N relationships backed by
    /// a synthetic intersect-entity table (mirroring how Dataverse itself stores teamroles /
    /// systemuserroles).
    /// </summary>
    public sealed class FakeOrganizationService : IOrganizationService
    {
        private static readonly Dictionary<string, (string Intersect, string FromColumn, string ToColumn)> RelationshipIntersects =
            new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
            {
                ["teamroles_association"] = ("teamroles", "teamid", "roleid"),
                ["systemuserroles_association"] = ("systemuserroles", "systemuserid", "roleid"),
            };

        private readonly Dictionary<string, Dictionary<Guid, Entity>> _tables =
            new Dictionary<string, Dictionary<Guid, Entity>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// When set, called before every Associate/Disassociate; returning true simulates a
        /// platform fault for that call (e.g. an Access Team rejecting a security-role
        /// association), without changing any state.
        /// </summary>
        public Func<string, Guid, Relationship, bool> FaultPredicate { get; set; }

        public Entity Seed(Entity entity)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
            GetOrCreateTable(entity.LogicalName)[entity.Id] = entity;
            return entity;
        }

        public Entity SeedTeam(Guid id, string name, Guid businessUnitId, string businessUnitName, string teamType)
        {
            var team = new Entity("team", id) { ["name"] = name };
            if (businessUnitId != Guid.Empty)
                team["businessunitid"] = new EntityReference("businessunit", businessUnitId) { Name = businessUnitName };
            team.FormattedValues["teamtype"] = teamType;
            return Seed(team);
        }

        public Entity SeedRole(Guid id, string name, Guid businessUnitId, string businessUnitName, Guid? rootRoleId = null)
        {
            var role = new Entity("role", id) { ["name"] = name };
            if (businessUnitId != Guid.Empty)
                role["businessunitid"] = new EntityReference("businessunit", businessUnitId) { Name = businessUnitName };
            if (rootRoleId.HasValue)
                role["parentrootroleid"] = new EntityReference("role", rootRoleId.Value);
            return Seed(role);
        }

        /// <summary>Directly seeds a teamroles intersect row, bypassing Associate (for arrange-time setup).</summary>
        public void SeedTeamRole(Guid teamId, Guid roleId)
        {
            var row = new Entity("teamroles", Guid.NewGuid());
            row["teamid"] = teamId;
            row["roleid"] = roleId;
            Seed(row);
        }

        public Guid Create(Entity entity)
        {
            if (entity.Id == Guid.Empty)
                entity.Id = Guid.NewGuid();
            GetOrCreateTable(entity.LogicalName)[entity.Id] = entity;
            return entity.Id;
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            if (_tables.TryGetValue(entityName, out var table) && table.TryGetValue(id, out var entity))
                return entity;
            throw new InvalidOperationException($"{entityName} {id} does not exist.");
        }

        public void Update(Entity entity)
        {
            var table = GetOrCreateTable(entity.LogicalName);
            if (!table.TryGetValue(entity.Id, out var existing))
                throw new InvalidOperationException($"{entity.LogicalName} {entity.Id} does not exist.");
            foreach (var attribute in entity.Attributes)
                existing[attribute.Key] = attribute.Value;
        }

        public void Delete(string entityName, Guid id)
        {
            if (_tables.TryGetValue(entityName, out var table))
                table.Remove(id);
        }

        public void Associate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            if (FaultPredicate != null && FaultPredicate(entityName, entityId, relationship))
                throw new InvalidOperationException(
                    $"Cannot associate: {entityName} {entityId} does not support the '{relationship.SchemaName}' relationship (simulated fault).");

            var (intersect, fromColumn, toColumn) = ResolveRelationship(relationship.SchemaName);
            var table = GetOrCreateTable(intersect);
            foreach (var related in relatedEntities)
            {
                var alreadyLinked = table.Values.Any(row =>
                    GetGuidValue(row, fromColumn) == entityId && GetGuidValue(row, toColumn) == related.Id);
                if (alreadyLinked)
                    continue;

                var row = new Entity(intersect, Guid.NewGuid());
                row[fromColumn] = entityId;
                row[toColumn] = related.Id;
                table[row.Id] = row;
            }
        }

        public void Disassociate(string entityName, Guid entityId, Relationship relationship, EntityReferenceCollection relatedEntities)
        {
            if (FaultPredicate != null && FaultPredicate(entityName, entityId, relationship))
                throw new InvalidOperationException(
                    $"Cannot disassociate: {entityName} {entityId} does not support the '{relationship.SchemaName}' relationship (simulated fault).");

            var (intersect, fromColumn, toColumn) = ResolveRelationship(relationship.SchemaName);
            if (!_tables.TryGetValue(intersect, out var table))
                return;

            var relatedIds = new HashSet<Guid>(relatedEntities.Select(r => r.Id));
            var idsToRemove = table.Values
                .Where(row => GetGuidValue(row, fromColumn) == entityId && relatedIds.Contains(GetGuidValue(row, toColumn)))
                .Select(row => row.Id)
                .ToList();
            foreach (var id in idsToRemove)
                table.Remove(id);
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            throw new NotSupportedException($"FakeOrganizationService does not implement Execute for '{request.RequestName}'.");
        }

        public EntityCollection RetrieveMultiple(QueryBase queryBase)
        {
            if (!(queryBase is QueryExpression query))
                throw new NotSupportedException("FakeOrganizationService only supports QueryExpression.");

            IEnumerable<Entity> rows = _tables.TryGetValue(query.EntityName, out var baseTable)
                ? baseTable.Values
                : Enumerable.Empty<Entity>();

            if (query.Criteria != null)
            {
                foreach (var condition in query.Criteria.Conditions)
                    rows = rows.Where(e => MatchesCondition(e, condition));
            }

            foreach (var link in query.LinkEntities)
            {
                var linkedRows = _tables.TryGetValue(link.LinkToEntityName, out var linkedTable)
                    ? (IEnumerable<Entity>)linkedTable.Values
                    : Enumerable.Empty<Entity>();

                if (link.LinkCriteria != null)
                {
                    foreach (var condition in link.LinkCriteria.Conditions)
                        linkedRows = linkedRows.Where(e => MatchesCondition(e, condition));
                }
                var linkedRowList = linkedRows.ToList();

                rows = rows.Where(baseRow =>
                {
                    var fromValue = GetGuidValue(baseRow, link.LinkFromAttributeName);
                    return linkedRowList.Any(linkedRow => GetGuidValue(linkedRow, link.LinkToAttributeName) == fromValue);
                });
            }

            var firstOrder = query.Orders.FirstOrDefault();
            if (firstOrder != null)
            {
                rows = firstOrder.OrderType == OrderType.Descending
                    ? rows.OrderByDescending(e => e.GetAttributeValue<object>(firstOrder.AttributeName) as IComparable)
                    : rows.OrderBy(e => e.GetAttributeValue<object>(firstOrder.AttributeName) as IComparable);
            }

            var all = rows.ToList();

            var pageNumber = query.PageInfo != null && query.PageInfo.PageNumber > 0 ? query.PageInfo.PageNumber : 1;
            var pageSize = query.PageInfo != null && query.PageInfo.Count > 0 ? query.PageInfo.Count : all.Count;
            var page = pageSize > 0
                ? all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList()
                : all;

            var moreRecords = pageSize > 0 && pageNumber * pageSize < all.Count;
            return new EntityCollection(page)
            {
                MoreRecords = moreRecords,
                PagingCookie = moreRecords ? $"page={pageNumber + 1}" : null,
                TotalRecordCount = all.Count
            };
        }

        private Dictionary<Guid, Entity> GetOrCreateTable(string logicalName)
        {
            if (!_tables.TryGetValue(logicalName, out var table))
            {
                table = new Dictionary<Guid, Entity>();
                _tables[logicalName] = table;
            }
            return table;
        }

        private static (string Intersect, string FromColumn, string ToColumn) ResolveRelationship(string schemaName)
        {
            if (!RelationshipIntersects.TryGetValue(schemaName, out var mapping))
                throw new NotSupportedException($"FakeOrganizationService does not know relationship '{schemaName}'.");
            return mapping;
        }

        /// <summary>
        /// Resolves an attribute to a Guid the way the join/filter logic needs: the entity's own
        /// primary-key attribute (e.g. "roleid" on a "role" row) falls back to Entity.Id, since
        /// seeded test entities don't duplicate that value into their attribute bag the way a real
        /// Dataverse row does; anything else reads the attribute (Guid or EntityReference.Id).
        /// </summary>
        private static Guid GetGuidValue(Entity entity, string attributeName)
        {
            if (string.Equals(attributeName, entity.LogicalName + "id", StringComparison.OrdinalIgnoreCase))
                return entity.Id;

            var value = entity.GetAttributeValue<object>(attributeName);
            switch (value)
            {
                case Guid guid:
                    return guid;
                case EntityReference reference:
                    return reference.Id;
                default:
                    return Guid.Empty;
            }
        }

        private static bool MatchesCondition(Entity entity, ConditionExpression condition)
        {
            if (condition.Operator != ConditionOperator.Equal || condition.Values.Count != 1)
                return true; // unsupported operators are permissive - out of scope for this fake

            if (condition.Values[0] is Guid expectedGuid)
                return GetGuidValue(entity, condition.AttributeName) == expectedGuid;

            var actual = entity.GetAttributeValue<object>(condition.AttributeName);
            return Equals(actual, condition.Values[0]);
        }
    }
}
