using System.Linq;
using Microsoft.CodeAnalysis;

namespace DataSurface.Generator;

/// <summary>
/// Roslyn symbol/attribute helper extensions used by the source generator.
/// </summary>
internal static class SymbolExtensions
{
    /// <summary>
    /// Gets the first attribute of the specified <paramref name="attrType"/> applied to the symbol.
    /// </summary>
    /// <param name="s">The symbol to inspect.</param>
    /// <param name="attrType">The attribute type to look for.</param>
    /// <returns>The matching attribute data if present; otherwise <c>null</c>.</returns>
    public static AttributeData? GetAttr(this ISymbol s, INamedTypeSymbol attrType)
        => s.GetAttributes().FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrType));

    /// <summary>
    /// Returns <see langword="true"/> if the symbol has an attribute of the specified <paramref name="attrType"/>.
    /// </summary>
    /// <param name="s">The symbol to inspect.</param>
    /// <param name="attrType">The attribute type to look for.</param>
    /// <returns><c>true</c> if present; otherwise <c>false</c>.</returns>
    public static bool HasAttr(this ISymbol s, INamedTypeSymbol attrType)
        => s.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attrType));

    /// <summary>
    /// Gets a named argument as a string.
    /// </summary>
    /// <param name="a">The attribute data.</param>
    /// <param name="name">The named argument key.</param>
    /// <returns>The string value if present; otherwise <c>null</c>.</returns>
    public static string? GetNamedArgString(this AttributeData a, string name)
    {
        foreach (var kv in a.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is string s) return s;
        }
        return null;
    }

    /// <summary>
    /// Gets a named argument as a boolean.
    /// </summary>
    /// <param name="a">The attribute data.</param>
    /// <param name="name">The named argument key.</param>
    /// <param name="fallback">The fallback value returned when the argument is not present.</param>
    /// <returns>The boolean value if present; otherwise <paramref name="fallback"/>.</returns>
    public static bool GetNamedArgBool(this AttributeData a, string name, bool fallback = false)
    {
        foreach (var kv in a.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is bool b) return b;
        }
        return fallback;
    }

    /// <summary>
    /// Gets a constructor argument as an integer.
    /// </summary>
    /// <param name="a">The attribute data.</param>
    /// <param name="index">The constructor argument index.</param>
    /// <returns>The integer value if present; otherwise <c>null</c>.</returns>
    public static int? GetCtorArgInt(this AttributeData a, int index)
    {
        if (a.ConstructorArguments.Length <= index) return null;
        return a.ConstructorArguments[index].Value as int?;
    }
}
