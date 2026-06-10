// Polyfill that enables C# records / init-only setters on netstandard2.0.
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved by the compiler for tracking init-only setter metadata; required when
    /// targeting netstandard2.0 with a modern language version.
    /// </summary>
    internal static class IsExternalInit
    {
    }
}
