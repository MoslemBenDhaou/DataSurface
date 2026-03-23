using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.Tests.Service.Shared.Builders;

/// <summary>
/// Fluent builder for <see cref="ResourceContract"/> test instances.
/// </summary>
public sealed class ResourceContractBuilder
{
    private string _resourceKey = "TestResource";
    private string _route = "test-resources";
    private StorageBackend _backend = StorageBackend.EfCore;
    private ResourceKeyContract _key = new("Id", FieldType.Int32);
    private int _maxPageSize = 100;
    private string? _defaultSort;
    private readonly List<FieldContract> _fields = new();
    private readonly List<RelationContract> _relations = new();
    private readonly Dictionary<CrudOperation, OperationContract> _operations = new();
    private readonly Dictionary<CrudOperation, string?> _policies = new();
    private readonly List<string> _expandAllowed = new();
    private int _maxExpandDepth = 1;
    private readonly List<string> _defaultExpand = new();
    private TenantContract? _tenant;
    private ConcurrencyContract? _concurrency;

    public ResourceContractBuilder() { }

    public ResourceContractBuilder(string resourceKey, string route)
    {
        _resourceKey = resourceKey;
        _route = route;
    }

    public ResourceContractBuilder ResourceKey(string value) { _resourceKey = value; return this; }
    public ResourceContractBuilder Route(string value) { _route = value; return this; }
    public ResourceContractBuilder Backend(StorageBackend value) { _backend = value; return this; }
    public ResourceContractBuilder Key(string name, FieldType type) { _key = new ResourceKeyContract(name, type); return this; }
    public ResourceContractBuilder MaxPageSize(int value) { _maxPageSize = value; return this; }
    public ResourceContractBuilder DefaultSort(string? value) { _defaultSort = value; return this; }
    public ResourceContractBuilder MaxExpandDepth(int value) { _maxExpandDepth = value; return this; }
    public ResourceContractBuilder Tenant(string fieldName, string fieldApiName, string claimType = "tenant_id", bool required = true)
    {
        _tenant = new TenantContract(fieldName, fieldApiName, claimType, required);
        return this;
    }

    public ResourceContractBuilder WithField(FieldContract field)
    {
        _fields.Add(field);
        return this;
    }

    public ResourceContractBuilder WithField(Action<FieldBuilder> configure, string name)
    {
        var builder = new FieldBuilder(name);
        configure(builder);
        _fields.Add(builder.Build());
        return this;
    }

    public ResourceContractBuilder WithRelation(RelationContract relation)
    {
        _relations.Add(relation);
        return this;
    }

    public ResourceContractBuilder WithExpandAllowed(params string[] apiNames)
    {
        _expandAllowed.AddRange(apiNames);
        return this;
    }

    public ResourceContractBuilder WithDefaultExpand(params string[] apiNames)
    {
        _defaultExpand.AddRange(apiNames);
        return this;
    }

    public ResourceContractBuilder WithOperation(CrudOperation op, bool enabled = true,
        IReadOnlyList<string>? inputShape = null,
        IReadOnlyList<string>? outputShape = null,
        IReadOnlyList<string>? requiredOnCreate = null,
        IReadOnlyList<string>? immutableFields = null,
        ConcurrencyContract? concurrency = null)
    {
        _operations[op] = new OperationContract(
            enabled,
            inputShape ?? Array.Empty<string>(),
            outputShape ?? Array.Empty<string>(),
            requiredOnCreate ?? Array.Empty<string>(),
            immutableFields ?? Array.Empty<string>(),
            concurrency);
        return this;
    }

    public ResourceContractBuilder WithPolicy(CrudOperation op, string? policy)
    {
        _policies[op] = policy;
        return this;
    }

    public ResourceContractBuilder WithConcurrency(ConcurrencyContract concurrency)
    {
        _concurrency = concurrency;
        return this;
    }

    public ResourceContractBuilder EnableAllOperations()
    {
        var readFields = _fields.Where(f => f.InRead).Select(f => f.ApiName).ToList();
        var createFields = _fields.Where(f => f.InCreate).Select(f => f.ApiName).ToList();
        var updateFields = _fields.Where(f => f.InUpdate).Select(f => f.ApiName).ToList();
        var requiredOnCreate = _fields.Where(f => f.Validation.RequiredOnCreate).Select(f => f.ApiName).ToList();
        var immutableFields = _fields.Where(f => f.Immutable).Select(f => f.ApiName).ToList();

        foreach (var op in Enum.GetValues<CrudOperation>())
        {
            if (_operations.ContainsKey(op)) continue;

            var input = op switch
            {
                CrudOperation.Create => createFields,
                CrudOperation.Update => updateFields,
                _ => Array.Empty<string>().ToList()
            };
            var output = op switch
            {
                CrudOperation.List or CrudOperation.Get or CrudOperation.Create or CrudOperation.Update => readFields,
                _ => Array.Empty<string>().ToList()
            };

            _operations[op] = new OperationContract(
                true, input, output, requiredOnCreate, immutableFields,
                op == CrudOperation.Update ? _concurrency : null);
        }
        return this;
    }

    public ResourceContract Build()
    {
        var filterableFields = _fields.Where(f => f.Filterable).Select(f => f.ApiName).ToList();
        var sortableFields = _fields.Where(f => f.Sortable).Select(f => f.ApiName).ToList();
        var searchableFields = _fields.Where(f => f.Searchable).Select(f => f.ApiName).ToList();

        // Auto-generate operations if none set
        if (_operations.Count == 0)
            EnableAllOperations();

        return new ResourceContract(
            ResourceKey: _resourceKey,
            Route: _route,
            Backend: _backend,
            Key: _key,
            Query: new QueryContract(_maxPageSize, filterableFields, sortableFields, searchableFields, _defaultSort),
            Read: new ReadContract(_expandAllowed, _maxExpandDepth, _defaultExpand),
            Fields: _fields,
            Relations: _relations,
            Operations: _operations,
            Security: new SecurityContract(_policies),
            Tenant: _tenant
        );
    }

    /// <summary>
    /// Creates a minimal valid resource contract with sensible defaults.
    /// </summary>
    public static ResourceContract Minimal(string resourceKey = "TestResource", string route = "test-resources")
    {
        return new ResourceContractBuilder(resourceKey, route)
            .WithField(new FieldBuilder("Id").OfType(FieldType.Int32).InRead().Filterable().Sortable().Build())
            .WithField(new FieldBuilder("Name").OfType(FieldType.String).ReadCreateUpdate().Filterable().Sortable().Searchable().RequiredOnCreate().Build())
            .EnableAllOperations()
            .Build();
    }
}
