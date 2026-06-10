using System;
using System.Collections;
using System.Collections.Generic;

namespace DataSurface.Generator;

/// <summary>
/// An immutable array wrapper with structural (per-element) equality, suitable for use inside
/// incremental generator models so the pipeline caches correctly.
/// </summary>
/// <typeparam name="T">The element type; must itself be value-equatable.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    private readonly T[]? _array;

    /// <summary>
    /// Wraps the given array. The array must not be mutated afterwards.
    /// </summary>
    /// <param name="array">The underlying array.</param>
    public EquatableArray(T[] array) => _array = array;

    /// <summary>
    /// Gets an empty array.
    /// </summary>
    public static EquatableArray<T> Empty => new(Array.Empty<T>());

    /// <summary>
    /// Gets the number of elements.
    /// </summary>
    public int Length => _array?.Length ?? 0;

    /// <summary>
    /// Gets the element at the given index.
    /// </summary>
    /// <param name="index">The zero-based index.</param>
    public T this[int index] => (_array ?? Array.Empty<T>())[index];

    /// <summary>
    /// Gets the underlying array (never null).
    /// </summary>
    public T[] UnderlyingArray => _array ?? Array.Empty<T>();

    /// <inheritdoc />
    public bool Equals(EquatableArray<T> other)
    {
        var a = _array ?? Array.Empty<T>();
        var b = other._array ?? Array.Empty<T>();
        if (ReferenceEquals(a, b)) return true;
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in _array ?? Array.Empty<T>())
            {
                hash = (hash * 31) + (item?.GetHashCode() ?? 0);
            }
            return hash;
        }
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)UnderlyingArray).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
