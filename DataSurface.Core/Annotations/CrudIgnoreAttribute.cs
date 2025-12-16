namespace DataSurface.Core.Annotations;

/// <summary>
/// Marks a property to be ignored by DataSurface contract generation.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class CrudIgnoreAttribute : Attribute
{
}
