using Microsoft.CodeAnalysis;

namespace DataSurface.Generator;

/// <summary>
/// Roslyn symbol/attribute helper extensions used by the source generator.
/// All attribute matching is done by fully qualified metadata name strings so the generator
/// never needs a reference to DataSurface.Core.
/// </summary>
internal static class SymbolExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> when the attribute's class matches the given fully qualified name.
    /// </summary>
    /// <param name="a">The attribute data.</param>
    /// <param name="fullName">Fully qualified attribute type name (for example "DataSurface.Core.Annotations.CrudFieldAttribute").</param>
    public static bool IsAttribute(this AttributeData a, string fullName)
        => a.AttributeClass?.ToDisplayString() == fullName;

    /// <summary>
    /// Gets the first attribute with the given fully qualified name applied to the symbol, or <c>null</c>.
    /// </summary>
    /// <param name="s">The symbol to inspect.</param>
    /// <param name="fullName">Fully qualified attribute type name.</param>
    public static AttributeData? FindAttribute(this ISymbol s, string fullName)
    {
        foreach (var a in s.GetAttributes())
        {
            if (a.IsAttribute(fullName)) return a;
        }
        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the symbol has an attribute with the given fully qualified name.
    /// </summary>
    /// <param name="s">The symbol to inspect.</param>
    /// <param name="fullName">Fully qualified attribute type name.</param>
    public static bool HasAttribute(this ISymbol s, string fullName)
        => s.FindAttribute(fullName) is not null;

    /// <summary>
    /// Gets a named argument as a string, or <c>null</c> when absent.
    /// </summary>
    /// <param name="a">The attribute data.</param>
    /// <param name="name">The named argument key.</param>
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
    /// <param name="fallback">The value returned when the argument is not present.</param>
    public static bool GetNamedArgBool(this AttributeData a, string name, bool fallback = false)
    {
        foreach (var kv in a.NamedArguments)
        {
            if (kv.Key == name && kv.Value.Value is bool b) return b;
        }
        return fallback;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the attribute carries the given named argument at all.
    /// </summary>
    /// <param name="a">The attribute data.</param>
    /// <param name="name">The named argument key.</param>
    public static bool HasNamedArg(this AttributeData a, string name)
    {
        foreach (var kv in a.NamedArguments)
        {
            if (kv.Key == name) return true;
        }
        return false;
    }
}
