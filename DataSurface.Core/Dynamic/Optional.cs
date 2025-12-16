namespace DataSurface.Core.Dynamic;

/// <summary>
/// Represents an optional value that can distinguish between "not provided" and "provided".
/// </summary>
/// <typeparam name="T">Underlying value type.</typeparam>
public readonly struct Optional<T>
{
    /// <summary>
    /// Gets whether this instance contains a value.
    /// </summary>
    public bool HasValue { get; }
    private readonly T? _value;

    /// <summary>
    /// Gets the contained value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="HasValue"/> is <see langword="false"/>.</exception>
    public T Value => HasValue
        ? _value!
        : throw new InvalidOperationException("Optional has no value.");

    /// <summary>
    /// Creates an optional containing the given <paramref name="value"/>.
    /// </summary>
    /// <param name="value">Value to store.</param>
    public Optional(T value)
    {
        HasValue = true;
        _value = value;
    }

    /// <summary>
    /// Returns an empty optional (no value).
    /// </summary>
    public static Optional<T> None() => default;
    /// <summary>
    /// Implicitly wraps <paramref name="value"/> in an <see cref="Optional{T}"/>.
    /// </summary>
    /// <param name="value">Value to wrap.</param>
    public static implicit operator Optional<T>(T value) => new(value);
}
