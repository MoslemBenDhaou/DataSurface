namespace DataSurface.Core.Annotations;

/// <summary>
/// Marks a property as hidden and excluded from the generated contract.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class CrudHiddenAttribute : Attribute { }