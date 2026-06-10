using DataSurface.Core.ContractBuilderModels;
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
    /// <exception cref="ContractValidationException">Thrown when the definition is invalid.</exception>
    public ResourceContract Build(EntityDef def)
    {
        // Dynamic definitions come from admin/runtime input, so validate before building:
        // this is the path where malformed metadata is most likely.
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(def.EntityKey))
            errors.Add("EntityKey is required.");
        if (string.IsNullOrWhiteSpace(def.Route))
            errors.Add($"Entity '{def.EntityKey}': Route is required.");
        if (string.IsNullOrWhiteSpace(def.KeyName))
            errors.Add($"Entity '{def.EntityKey}': KeyName is required.");
        if (def.MaxPageSize < 1)
            errors.Add($"Entity '{def.EntityKey}': MaxPageSize must be at least 1 (was {def.MaxPageSize}).");
        if (def.MaxExpandDepth < 0 || def.MaxExpandDepth > 3)
            errors.Add($"Entity '{def.EntityKey}': MaxExpandDepth {def.MaxExpandDepth} is out of the supported range 0..3.");

        foreach (var dup in def.Properties.GroupBy(p => p.ApiName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Entity '{def.EntityKey}': duplicate property apiName '{dup.Key}'.");

        var propApiNames = new HashSet<string>(def.Properties.Select(p => p.ApiName), StringComparer.OrdinalIgnoreCase);
        foreach (var r in def.Relations)
        {
            if (propApiNames.Contains(r.ApiName))
                errors.Add($"Entity '{def.EntityKey}': relation '{r.ApiName}' collides with a property of the same apiName.");
            if (r.DefaultExpanded && !r.ExpandAllowed)
                errors.Add($"Entity '{def.EntityKey}': relation '{r.ApiName}' has DefaultExpanded but ExpandAllowed is false; the default expansion would never happen.");
        }

        var concurrencyProps = def.Properties.Where(p => p.ConcurrencyToken).ToList();
        if (concurrencyProps.Count > 1)
            errors.Add($"Entity '{def.EntityKey}': multiple concurrency-token properties ({string.Join(", ", concurrencyProps.Select(p => p.ApiName))}); only one is allowed.");

        if (errors.Count > 0)
            throw new ContractValidationException(errors);

        var key = new ResourceKeyContract(def.KeyName, def.KeyType);

        // Authorization is opt-in: only apply policies if explicitly configured
        var policies = def.Policies ?? new Dictionary<CrudOperation, string?>();

        ConcurrencyContract? concurrency = null;

        var fields = def.Properties.Select(p =>
        {
            if (p.ConcurrencyToken)
                concurrency = new ConcurrencyContract(p.ConcurrencyMode, p.ApiName, p.ConcurrencyRequiredOnUpdate);

            var v = new FieldValidationContract(p.RequiredOnCreate, p.MinLength, p.MaxLength, p.Min, p.Max, p.Regex, p.AllowedValues);

            return new FieldContract(
                Name: p.Name,
                ApiName: p.ApiName,
                Type: p.Type,
                Nullable: p.Nullable,
                InRead: p.In.HasFlag(CrudDto.Read) && !p.Hidden,
                InCreate: p.In.HasFlag(CrudDto.Create) && !p.Hidden && !p.Computed,
                InUpdate: p.In.HasFlag(CrudDto.Update) && !p.Hidden && !p.Immutable && !p.Computed,
                Filterable: p.In.HasFlag(CrudDto.Filter) && !p.Hidden,
                Sortable: p.In.HasFlag(CrudDto.Sort) && !p.Hidden,
                Hidden: p.Hidden,
                Immutable: p.Immutable || p.Computed || p.Name.Equals(def.KeyName, StringComparison.OrdinalIgnoreCase),
                Searchable: p.Searchable && !p.Hidden,
                Computed: p.Computed,
                ComputedExpression: p.ComputedExpression,
                DefaultValue: p.DefaultValue,
                Validation: v
            );
        }).ToList();

        // The tenant discriminator is server-managed: never client-writable.
        if (def.Tenant is not null)
        {
            for (var i = 0; i < fields.Count; i++)
            {
                if (string.Equals(fields[i].ApiName, def.Tenant.FieldApiName, StringComparison.OrdinalIgnoreCase) &&
                    (fields[i].InCreate || fields[i].InUpdate || !fields[i].Immutable))
                {
                    fields[i] = fields[i] with { InCreate = false, InUpdate = false, Immutable = true };
                }
            }
        }

        if (fields.All(f => !f.Name.Equals(def.KeyName, StringComparison.OrdinalIgnoreCase)))
        {
            fields.Add(new FieldContract(
                Name: def.KeyName,
                ApiName: char.ToLowerInvariant(def.KeyName[0]) + def.KeyName[1..],
                Type: def.KeyType,
                Nullable: false,
                InRead: true,
                InCreate: false,
                InUpdate: false,
                Filterable: true,
                Sortable: true,
                Hidden: false,
                Immutable: true,
                Searchable: false,
                Computed: false,
                ComputedExpression: null,
                DefaultValue: null,
                Validation: new FieldValidationContract(false, null, null, null, null, null)
            ));
        }

        // injected key may still collide with an existing field's apiName
        foreach (var dup in fields.GroupBy(f => f.ApiName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add($"Entity '{def.EntityKey}': duplicate field apiName '{dup.Key}'.");
        if (errors.Count > 0)
            throw new ContractValidationException(errors);

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
        var searchableFields = fields.Where(f => f.Searchable).Select(f => f.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var expandAllowed = relations.Where(r => r.Read.ExpandAllowed).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var defaultExpand = relations.Where(r => r.Read.DefaultExpanded).Select(r => r.ApiName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var query = new QueryContract(def.MaxPageSize, filterableFields, sortableFields, searchableFields, DefaultSort: null);
        var read = new ReadContract(expandAllowed, def.MaxExpandDepth, defaultExpand);

        IReadOnlyList<string> readShape = fields.Where(f => f.InRead).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> createShape = fields.Where(f => f.InCreate).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> updateShape = fields.Where(f => f.InUpdate).Select(f => f.ApiName).ToList();

        var readOutput = readShape.Concat(expandAllowed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        IReadOnlyList<string> requiredOnCreate = fields.Where(f => f.Validation.RequiredOnCreate).Select(f => f.ApiName).ToList();
        IReadOnlyList<string> immutableFields = fields.Where(f => f.Immutable).Select(f => f.ApiName).ToList();

        var ops = new Dictionary<CrudOperation, OperationContract>
        {
            [CrudOperation.List] = new(def.EnableList,  Array.Empty<string>(), readOutput, Array.Empty<string>(), immutableFields, null),
            [CrudOperation.Get]  = new(def.EnableGet,   Array.Empty<string>(), readOutput, Array.Empty<string>(), immutableFields, null),
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
            Security: new SecurityContract(policies),
            Tenant: def.Tenant
        );
    }
}
