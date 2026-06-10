; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DSG001 | DataSurface.Generator | Error | Missing route on [CrudResource]
DSG002 | DataSurface.Generator | Error | Duplicate API name
DSG003 | DataSurface.Generator | Error | Missing key property
DSG004 | DataSurface.Generator | Error | Multiple [CrudKey] properties
DSG005 | DataSurface.Generator | Error | [CrudIgnore] conflicts with [CrudField]
DSG006 | DataSurface.Generator | Error | Generic resource types not supported
DSG007 | DataSurface.Generator | Error | ApiName is not a valid identifier
DSG008 | DataSurface.Generator | Error | KeyProperty override not found
DSG009 | DataSurface.Generator | Error | [CrudField] on navigation property
