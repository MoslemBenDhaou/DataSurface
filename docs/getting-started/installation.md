# Installation

## Requirements

- **.NET 9.0** or later
- **Entity Framework Core 9.0** (for EF Core backend)
- An ASP.NET Core application (for HTTP endpoints)

## NuGet Packages

Install the packages you need via the .NET CLI or your package manager:

```bash
# Core — always required
dotnet add package DataSurface.Core

# EF Core backend — for static entities backed by Entity Framework
dotnet add package DataSurface.EFCore

# HTTP layer — for REST endpoint generation via Minimal APIs
dotnet add package DataSurface.Http

# Dynamic entities — for runtime-defined resources
dotnet add package DataSurface.Dynamic

# Admin API — for managing dynamic entity definitions via REST
dotnet add package DataSurface.Admin

# OpenAPI — for Swashbuckle/Swagger integration
dotnet add package DataSurface.OpenApi

# Source generator (optional) — for typed DTO generation
dotnet add package DataSurface.Generator
```

## Package Combinations

Choose the combination that matches your use case:

### Static Resources Only

For compile-time entities backed by EF Core:

```xml
<ItemGroup>
  <PackageReference Include="DataSurface.Core" Version="*" />
  <PackageReference Include="DataSurface.EFCore" Version="*" />
  <PackageReference Include="DataSurface.Http" Version="*" />
</ItemGroup>
```

### Dynamic Resources Only

For entities defined at runtime via database metadata:

```xml
<ItemGroup>
  <PackageReference Include="DataSurface.Core" Version="*" />
  <PackageReference Include="DataSurface.Dynamic" Version="*" />
  <PackageReference Include="DataSurface.Http" Version="*" />
  <PackageReference Include="DataSurface.Admin" Version="*" />
</ItemGroup>
```

### Both Static and Dynamic

```xml
<ItemGroup>
  <PackageReference Include="DataSurface.Core" Version="*" />
  <PackageReference Include="DataSurface.EFCore" Version="*" />
  <PackageReference Include="DataSurface.Dynamic" Version="*" />
  <PackageReference Include="DataSurface.Http" Version="*" />
  <PackageReference Include="DataSurface.Admin" Version="*" />
</ItemGroup>
```

### Optional Add-ons

Add these to any combination above:

```xml
<!-- Swagger/OpenAPI typed schemas -->
<PackageReference Include="DataSurface.OpenApi" Version="*" />

<!-- Compile-time typed DTO generation -->
<PackageReference Include="DataSurface.Generator" Version="*" />
```

## Next Step

→ [Quick Start](quick-start.md) — Build your first DataSurface API in under 5 minutes.
