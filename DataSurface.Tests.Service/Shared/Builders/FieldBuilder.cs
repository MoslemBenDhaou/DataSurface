using DataSurface.Core.Contracts;
using DataSurface.Core.Enums;

namespace DataSurface.Tests.Service.Shared.Builders;

/// <summary>
/// Fluent builder for <see cref="FieldContract"/> test instances.
/// </summary>
public sealed class FieldBuilder
{
    private string _name = "Field";
    private string _apiName = "field";
    private FieldType _type = FieldType.String;
    private bool _nullable;
    private bool _inRead = true;
    private bool _inCreate;
    private bool _inUpdate;
    private bool _filterable;
    private bool _sortable;
    private bool _hidden;
    private bool _immutable;
    private bool _searchable;
    private bool _computed;
    private string? _computedExpression;
    private object? _defaultValue;
    private bool _requiredOnCreate;
    private int? _minLength;
    private int? _maxLength;
    private decimal? _min;
    private decimal? _max;
    private string? _regex;
    private IReadOnlyList<string>? _allowedValues;

    public FieldBuilder(string name)
    {
        _name = name;
        _apiName = char.ToLowerInvariant(name[0]) + name[1..];
    }

    public FieldBuilder ApiName(string apiName) { _apiName = apiName; return this; }
    public FieldBuilder OfType(FieldType type) { _type = type; return this; }
    public FieldBuilder Nullable(bool value = true) { _nullable = value; return this; }
    public FieldBuilder InRead(bool value = true) { _inRead = value; return this; }
    public FieldBuilder InCreate(bool value = true) { _inCreate = value; return this; }
    public FieldBuilder InUpdate(bool value = true) { _inUpdate = value; return this; }
    public FieldBuilder Filterable(bool value = true) { _filterable = value; return this; }
    public FieldBuilder Sortable(bool value = true) { _sortable = value; return this; }
    public FieldBuilder Hidden(bool value = true) { _hidden = value; return this; }
    public FieldBuilder Immutable(bool value = true) { _immutable = value; return this; }
    public FieldBuilder Searchable(bool value = true) { _searchable = value; return this; }
    public FieldBuilder Computed(string expression) { _computed = true; _computedExpression = expression; return this; }
    public FieldBuilder DefaultValue(object? value) { _defaultValue = value; return this; }
    public FieldBuilder RequiredOnCreate(bool value = true) { _requiredOnCreate = value; return this; }
    public FieldBuilder MinLength(int value) { _minLength = value; return this; }
    public FieldBuilder MaxLength(int value) { _maxLength = value; return this; }
    public FieldBuilder Min(decimal value) { _min = value; return this; }
    public FieldBuilder Max(decimal value) { _max = value; return this; }
    public FieldBuilder Regex(string pattern) { _regex = pattern; return this; }
    public FieldBuilder AllowedValues(params string[] values) { _allowedValues = values; return this; }

    public FieldBuilder ReadCreateUpdate()
    {
        _inRead = true;
        _inCreate = true;
        _inUpdate = true;
        return this;
    }

    public FieldBuilder ReadOnly()
    {
        _inRead = true;
        _inCreate = false;
        _inUpdate = false;
        return this;
    }

    public FieldBuilder All()
    {
        _inRead = true;
        _inCreate = true;
        _inUpdate = true;
        _filterable = true;
        _sortable = true;
        return this;
    }

    public FieldContract Build()
    {
        return new FieldContract(
            Name: _name,
            ApiName: _apiName,
            Type: _type,
            Nullable: _nullable,
            InRead: _inRead,
            InCreate: _inCreate,
            InUpdate: _inUpdate,
            Filterable: _filterable,
            Sortable: _sortable,
            Hidden: _hidden,
            Immutable: _immutable,
            Searchable: _searchable,
            Computed: _computed,
            ComputedExpression: _computedExpression,
            DefaultValue: _defaultValue,
            Validation: new FieldValidationContract(
                _requiredOnCreate, _minLength, _maxLength, _min, _max, _regex, _allowedValues)
        );
    }
}
