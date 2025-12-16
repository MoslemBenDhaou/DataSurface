using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.Core;

// Runtime definitions (your system can have more fields; only what Core needs here)

/// <summary>
/// Builds <see cref="ResourceContract"/> instances from runtime (dynamic) resource definitions.
/// </summary>
public sealed class DynamicContractBuilder
{
    /// <summary>
    /// Converts an <see cref="EntityDef"/> into a normalized <see cref="ResourceContract"/>.
    /// </summary>
    /// <param name="def">Runtime definition describing the resource, fields, relations, and operations.</param>
    /// <returns>A normalized resource contract suitable for use by higher layers.</returns>
    public ResourceContract Build(EntityDef def)
    {
        var key = new ResourceKeyContract(def.KeyName, def.KeyType);

        var policies = def.Policies ?? new Dictionary<CrudOperation, string?>()
        {
            [CrudOperation.List]   = $"{def.Route}.read",
            [CrudOperation.Get]    = $"{def.Route}.read",
            [CrudOperation.Create] = $"{def.Route}.create",
            [CrudOperation.Update] = $"{def.Route}.update",
            [CrudOperation.Delete] = $"{def.Route}.delete",
        };

        ConcurrencyContract? concurrency = null;

        var fields = def.Properties.Select(p =>
        {
            if (p.ConcurrencyToken)
                concurrency = new ConcurrencyContract(p.ConcurrencyMode, p.ApiName, p.ConcurrencyRequiredOnUpdate);

            var v = new FieldValidationContract(p.RequiredOnCreate, p.MinLength, p.MaxLength, p.Min, p.Max, p.Regex);

            return new FieldContract(
                Name: p.Name,
                ApiName: p.ApiName,
                Type: p.Type,
                Nullable: p.Nullable,
                InRead: p.In.HasFlag(CrudDto.Read) && !p.Hidden,
                InCreate: p.In.HasFlag(CrudDto.Create) && !p.Hidden,
                InUpdate: p.In.HasFlag(CrudDto.Update) && !p.Hidden && !p.Immutable,
                Filterable: p.In.HasFlag(CrudDto.Filter) && !p.Hidden,
                Sortable: p.In.HasFlag(CrudDto.Sort) && !p.Hidden,
                Hidden: p.Hidden,
                Immutable: p.Immutable || p.Name.Equals(def.KeyName, StringComparison.OrdinalIgnoreCase),
                Validation: v
            );
        }).ToList();

        if (fields.All(f => !f.Name.Equals(def.KeyName, StringComparison.OrdinalIgnoreCase)))
        {
            fields.Add(new FieldContract(
                def.KeyName, char.ToLowerInvariant(def.KeyName[0]) + def.KeyName[1..],
                def.KeyType, false,
                InRead: true, InCreate: false, InUpdate: false,
                Filterable: true, Sortable: true,
                Hidden: false, Immutable: true,
                new FieldValidationContract(false, null, null, null, null, null)
            ));
        }

        var relations = def.Relations.Select(r => new RelationContract(
            Name: r.Name,
            ApiName: r.ApiName,
            Kind: r.Kind,
            TargetResourceKey: r.TargetResourceKey,
            Read: new RelationReadContract(r.ExpandAllowed, r.DefaultExpanded),
            Write: new RelationWriteContract(r.WriteMode, r.WriteFieldName, r.RequiredOnCreate, r.ForeignKeyProperty)
        )).ToList();

        var filterableFields = fields.Where(f => f.Filterable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var sortableFields = fields.Where(f => f.Sortable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var expandAllowed = relations.Where(r => r.Read.ExpandAllowed).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var defaultExpand = relations.Where(r => r.Read.DefaultExpanded).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var query = new QueryContract(def.MaxPageSize, filterableFields, sortableFields, DefaultSort: null);
        var read = new ReadContract(expandAllowed, def.MaxExpandDepth, defaultExpand);

        IReadOnlyList<string> readShape = fields.Where(f => f.InRead).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> createShape = fields.Where(f => f.InCreate).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> updateShape = fields.Where(f => f.InUpdate).Select(f => f.ApiName).ToList();

        var getOutput = readShape.Concat(expandAllowed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        IReadOnlyList<string> requiredOnCreate = fields.Where(f => f.Validation.RequiredOnCreate).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> immutableFields = fields.Where(f => f.Immutable).Select(f => f.ApiName).ToList();

        var ops = new Dictionary<CrudOperation, OperationContract>
        {
            [CrudOperation.List] = new(def.EnableList,  Array.Empty<string>(), readShape, Array.Empty<string>(), immutableFields, null),
            [CrudOperation.Get]  = new(def.EnableGet,   Array.Empty<string>(), getOutput, Array.Empty<string>(), immutableFields, null),
            [CrudOperation.Create]=new(def.EnableCreate,createShape, readShape, requiredOnCreate, immutableFields, null),
            [CrudOperation.Update]=new(def.EnableUpdate,updateShape, readShape, Array.Empty<string>(), immutableFields, concurrency),
            [CrudOperation.Delete]=new(def.EnableDelete,Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), immutableFields, null),
        };

        return new ResourceContract(
            ResourceKey: def.EntityKey,
            Route: def.Route,
            Backend: def.Backend,
            Key: key,
            Query: query,
            Read: read,
            Fields: fields,
            Relations: relations,
            Operations: ops,
            Security: new SecurityContract(policies)
        );
    }
}
