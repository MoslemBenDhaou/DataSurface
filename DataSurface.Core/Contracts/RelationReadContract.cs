namespace DataSurface.Core.Contracts;

/// <summary>
/// Read behavior for a relation, including expansion rules.
/// </summary>
/// <param name="ExpandAllowed">Whether the relation may be expanded during reads.</param>
/// <param name="DefaultExpanded">Whether the relation is expanded by default.</param>
public sealed record RelationReadContract(bool ExpandAllowed, bool DefaultExpanded);