using DataSurface.Core.Enums;

namespace DataSurface.Core.Contracts;

/// <summary>
/// Describes the resource primary key field in the unified contract.
/// </summary>
/// <param name="Name">CLR property name of the key.</param>
/// <param name="Type">Canonical contract field type of the key.</param>
public sealed record ResourceKeyContract(string Name, FieldType Type);