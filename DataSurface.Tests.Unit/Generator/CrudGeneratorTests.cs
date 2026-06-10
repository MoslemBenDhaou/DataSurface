using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataSurface.Generator;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataSurface.Tests.Unit.Generator;

/// <summary>
/// Drives <see cref="CrudGenerator"/> over an in-memory compilation, compiles the generated
/// output, and verifies the DTO/endpoint semantics match the runtime ContractBuilder:
/// key discovery ([CrudKey]/KeyProperty/Id/{TypeName}Id), inherited properties, [CrudIgnore],
/// [CrudHidden], Enable* flags, [CrudAuthorize] policies, JsonPropertyName round-tripping,
/// hint-name collision safety, record entities, and global-namespace types.
/// </summary>
public class CrudGeneratorTests
{
    private sealed record RunResult(
        GeneratorDriverRunResult Generator,
        Compilation Output)
    {
        public IReadOnlyList<GeneratedSourceResult> Sources { get; } =
            Generator.Results.SelectMany(r => r.GeneratedSources).ToList();

        public string AllText => string.Join("\n", Sources.Select(s => s.SourceText.ToString()));

        public string MapperText => Sources
            .Where(s => s.HintName.Contains("DataSurfaceGeneratedCrudEndpoints"))
            .Select(s => s.SourceText.ToString())
            .SingleOrDefault() ?? string.Empty;
    }

    private static readonly Lazy<IReadOnlyList<PortableExecutableReference>> AllReferences = new(() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => !string.IsNullOrWhiteSpace(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(p => MetadataReference.CreateFromFile(p))
        .ToList());

    /// <summary>
    /// Runs the generator and returns both the run result and the updated (output) compilation.
    /// DataSurface.EFCore is excluded from the references by default so the endpoint mapper is
    /// only generated when a test opts in.
    /// </summary>
    private static RunResult Run(string source, bool includeEfCore = false)
    {
        var references = AllReferences.Value
            .Where(r => includeEfCore || !r.FilePath!.EndsWith("DataSurface.EFCore.dll", StringComparison.OrdinalIgnoreCase))
            .Cast<MetadataReference>()
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAsm",
            new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CrudGenerator().AsSourceGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        return new RunResult(driver.GetRunResult(), output);
    }

    /// <summary>
    /// Asserts that the input source plus all generated trees compile without a single
    /// error-level diagnostic, and that no warnings originate from generated code.
    /// </summary>
    private static void AssertCompilesClean(RunResult run)
    {
        var diagnostics = run.Output.GetDiagnostics();

        diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Should().BeEmpty("the generated code must compile cleanly");

        var generatedPaths = new HashSet<string>(run.Sources.Select(s => s.SyntaxTree.FilePath));
        diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning
                        && d.Location.SourceTree is not null
                        && generatedPaths.Contains(d.Location.SourceTree.FilePath))
            .Should().BeEmpty("generated code must not produce warnings");
    }

    // -------------------------------------------------------------------------------------
    // Key discovery
    // -------------------------------------------------------------------------------------

    [Fact]
    public void CrudKey_Discovery_Honors_ApiName()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                [CrudKey(ApiName = "widgetKey")]
                public int Code { get; set; }

                [CrudField(CrudDto.Read)]
                public string Name { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.AllText.Should().Contain("JsonPropertyName(\"widgetKey\")");
        run.AllText.Should().Contain("public int WidgetKey { get; set; }");
    }

    [Fact]
    public void KeyProperty_Override_Wins_Over_Conventions()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets", KeyProperty = nameof(Code))]
            public class Widget
            {
                public int Id { get; set; }
                public string Code { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        // The key (Code) is synthesized into the Read DTO; the unannotated Id property is not.
        run.AllText.Should().Contain("JsonPropertyName(\"code\")");
        run.AllText.Should().NotContain("JsonPropertyName(\"id\")");
    }

    [Fact]
    public void TypeNameId_Fallback_Is_Used_When_No_Id_Property_Exists()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                public long WidgetId { get; set; }

                [CrudField(CrudDto.Read)]
                public string Name { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.AllText.Should().Contain("JsonPropertyName(\"widgetId\")");
        run.AllText.Should().Contain("public long WidgetId { get; set; }");
    }

    [Fact]
    public void Multiple_CrudKey_Properties_Report_DSG004()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                [CrudKey] public int Id { get; set; }
                [CrudKey] public int OtherId { get; set; }
            }
            """);

        run.Generator.Diagnostics.Should().ContainSingle(d => d.Id == "DSG004");
        run.Generator.Diagnostics.Single(d => d.Id == "DSG004").Location.Should().NotBe(Location.None);
        run.Sources.Should().BeEmpty("a resource with an ambiguous key must not emit code");
    }

    // -------------------------------------------------------------------------------------
    // Property semantics
    // -------------------------------------------------------------------------------------

    [Fact]
    public void Inherited_Properties_Are_Included()
    {
        var run = Run("""
            using System;
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            public abstract class EntityBase
            {
                public Guid Id { get; set; }

                [CrudField(CrudDto.Read)]
                public DateTime CreatedAt { get; set; }
            }

            [CrudResource("widgets")]
            public class Widget : EntityBase
            {
                [CrudField(CrudDto.Read | CrudDto.Create)]
                public string Name { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.AllText.Should().Contain("JsonPropertyName(\"createdAt\")");
        run.AllText.Should().Contain("JsonPropertyName(\"id\")", "the inherited Id is the discovered key");
    }

    [Fact]
    public void CrudIgnore_Excludes_Property_And_Conflicts_With_CrudField()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                public int Id { get; set; }

                [CrudIgnore]
                public string Internal { get; set; } = "";

                [CrudIgnore]
                [CrudField(CrudDto.Read)]
                public string Broken { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().ContainSingle(d => d.Id == "DSG005");
        run.AllText.Should().NotContain("internal\"");
        run.AllText.Should().NotContain("JsonPropertyName(\"broken\")");
    }

    [Fact]
    public void CrudHidden_Excludes_Property_From_All_Dto_Shapes()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                public int Id { get; set; }

                [CrudHidden]
                [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
                public string Secret { get; set; } = "";

                [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, Hidden = true)]
                public string AlsoSecret { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.AllText.Should().NotContain("secret");
        run.AllText.Should().NotContain("alsoSecret");
    }

    [Fact]
    public void Tenant_Field_Is_Never_Writable()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                public int Id { get; set; }

                [CrudTenant]
                [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
                public string TenantId { get; set; } = "";

                [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
                public string Name { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        var dtoSource = run.Sources.Single(s => s.HintName.Contains("Widget")).SourceText.ToString();

        // tenantId appears exactly once (Read DTO); name appears in all three shapes.
        CountOccurrences(dtoSource, "JsonPropertyName(\"tenantId\")").Should().Be(1);
        CountOccurrences(dtoSource, "JsonPropertyName(\"name\")").Should().Be(3);
    }

    [Fact]
    public void Every_Generated_Dto_Property_Carries_JsonPropertyName()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                public int Id { get; set; }

                [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update, ApiName = "displayName")]
                public string Name { get; set; } = "";

                [CrudField(CrudDto.Read, RequiredOnCreate = true)]
                public decimal? Price { get; set; }
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        var dtoSource = run.Sources.Single(s => s.HintName.Contains("Widget")).SourceText.ToString();

        var propertyCount = CountOccurrences(dtoSource, "{ get; set; }");
        var jsonNameCount = CountOccurrences(dtoSource, "global::System.Text.Json.Serialization.JsonPropertyName(");
        propertyCount.Should().BeGreaterThan(0);
        jsonNameCount.Should().Be(propertyCount, "every generated DTO property must carry [JsonPropertyName]");

        dtoSource.Should().Contain("JsonPropertyName(\"displayName\")");
        dtoSource.Should().Contain("public string DisplayName { get; set; }");
    }

    // -------------------------------------------------------------------------------------
    // Shapes of emitted types
    // -------------------------------------------------------------------------------------

    [Fact]
    public void Record_Entities_Are_Supported()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public sealed record Widget
            {
                [CrudKey]
                public int Id { get; init; }

                [CrudField(CrudDto.Read | CrudDto.Create)]
                public string Name { get; init; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.AllText.Should().Contain("class WidgetReadDto");
        run.AllText.Should().Contain("JsonPropertyName(\"name\")");
        run.AllText.Should().NotContain("EqualityContract", "compiler-synthesized record members must not leak into DTOs");
    }

    [Fact]
    public void Global_Namespace_Types_Are_Handled()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            [CrudResource("widgets")]
            public class GlobalWidget
            {
                public int Id { get; set; }

                [CrudField(CrudDto.Read)]
                public string Name { get; set; } = "";
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.AllText.Should().Contain("namespace DataSurfaceGenerated");
        run.AllText.Should().NotContain("GlobalNamespace.DataSurfaceGenerated");
        run.Sources.Should().ContainSingle(s => s.HintName.StartsWith("GlobalWidget"));
    }

    [Fact]
    public void SameNamed_Classes_In_Different_Namespaces_Do_Not_Collide()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace AppOne
            {
                [CrudResource("one-widgets")]
                public class Widget
                {
                    public int Id { get; set; }

                    [CrudField(CrudDto.Read)]
                    public string Name { get; set; } = "";
                }
            }

            namespace AppTwo
            {
                [CrudResource("two-widgets", ResourceKey = "WidgetTwo")]
                public class Widget
                {
                    public int Id { get; set; }

                    [CrudField(CrudDto.Read)]
                    public string Name { get; set; } = "";
                }
            }
            """);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        var hints = run.Sources.Select(s => s.HintName).ToList();
        hints.Should().OnlyHaveUniqueItems();
        hints.Should().Contain(h => h.StartsWith("AppOne.Widget"));
        hints.Should().Contain(h => h.StartsWith("AppTwo.Widget"));

        run.AllText.Should().Contain("namespace AppOne.DataSurfaceGenerated");
        run.AllText.Should().Contain("namespace AppTwo.DataSurfaceGenerated");
    }

    [Fact]
    public void Generic_Entities_Report_DSG006_And_Skip_Emission()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget<T>
            {
                public int Id { get; set; }
            }
            """);

        run.Generator.Diagnostics.Should().ContainSingle(d => d.Id == "DSG006");
        run.Sources.Should().BeEmpty();
    }

    [Fact]
    public void No_CrudResources_Means_No_Output()
    {
        var run = Run("""
            namespace TestApp;

            public class NotAResource
            {
                public int Id { get; set; }
            }
            """, includeEfCore: true);

        run.Generator.Diagnostics.Should().BeEmpty();
        run.Sources.Should().BeEmpty("the generator must stay silent when there are no resources");
    }

    // -------------------------------------------------------------------------------------
    // Endpoint mapper
    // -------------------------------------------------------------------------------------

    private const string EndpointSource = """
        using DataSurface.Core.Annotations;
        using DataSurface.Core.Enums;

        namespace TestApp;

        [CrudResource("widgets", EnableDelete = false, EnableUpdate = false)]
        [CrudAuthorize("WidgetAdmin")]
        [CrudAuthorize("WidgetReader", Operation = CrudOperation.List)]
        public class Widget
        {
            [CrudKey]
            public int Id { get; set; }

            [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
            public string Name { get; set; } = "";
        }
        """;

    [Fact]
    public void EndpointMapper_Is_Not_Emitted_Without_EfCore_Reference()
    {
        var run = Run(EndpointSource, includeEfCore: false);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.MapperText.Should().BeEmpty("the endpoint mapper depends on DataSurface.EFCore types");
        run.AllText.Should().Contain("class WidgetReadDto", "DTOs are still useful without the EF Core backend");
    }

    [Fact]
    public void Enable_Flags_Suppress_Disabled_Endpoints()
    {
        var run = Run(EndpointSource, includeEfCore: true);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        run.MapperText.Should().Contain("MapDataSurfaceGeneratedCrud");
        run.MapperText.Should().Contain("g.MapGet(\"/widgets\"");
        run.MapperText.Should().Contain("g.MapGet(\"/widgets/{id}\"");
        run.MapperText.Should().Contain("g.MapPost(\"/widgets\"");
        run.MapperText.Should().NotContain("MapDelete", "EnableDelete = false");
        run.MapperText.Should().NotContain("PATCH", "EnableUpdate = false");
    }

    [Fact]
    public void Authorization_Policies_Apply_AllOps_Then_PerOp_Overrides()
    {
        var run = Run(EndpointSource, includeEfCore: true);

        // The List route gets the per-operation override; Get/Create keep the all-ops policy.
        var listBlock = ExtractEndpointBlock(run.MapperText, "g.MapGet(\"/widgets\"");
        listBlock.Should().Contain(".RequireAuthorization(\"WidgetReader\")");

        var getBlock = ExtractEndpointBlock(run.MapperText, "g.MapGet(\"/widgets/{id}\"");
        getBlock.Should().Contain(".RequireAuthorization(\"WidgetAdmin\")");

        var createBlock = ExtractEndpointBlock(run.MapperText, "g.MapPost(\"/widgets\"");
        createBlock.Should().Contain(".RequireAuthorization(\"WidgetAdmin\")");
    }

    [Fact]
    public void Create_Returns_Location_With_New_Entity_Id_And_Patch_Binds_Raw_Json()
    {
        var run = Run("""
            using DataSurface.Core.Annotations;
            using DataSurface.Core.Enums;

            namespace TestApp;

            [CrudResource("widgets")]
            public class Widget
            {
                [CrudKey]
                public int Id { get; set; }

                [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
                public string Name { get; set; } = "";
            }
            """, includeEfCore: true);

        run.Generator.Diagnostics.Should().BeEmpty();
        AssertCompilesClean(run);

        // The Created location must point at the new entity, not the collection route.
        run.MapperText.Should().Contain("Results.Created($\"{req.Path}/{dto.Id}\", dto)");

        // Sparse PATCH: the handler binds the raw JSON body so an omitted property is
        // distinguishable from an explicit null, instead of serializing a typed DTO.
        run.MapperText.Should().Contain("JsonObject patch");
        run.MapperText.Should().NotContain("SerializeToNode(patch)");
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    private static int CountOccurrences(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }

    /// <summary>
    /// Extracts a single endpoint registration block (up to its terminating ";") so policy
    /// assertions cannot accidentally match a neighboring route.
    /// </summary>
    private static string ExtractEndpointBlock(string mapperText, string startToken)
    {
        var start = mapperText.IndexOf(startToken, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"expected mapper to contain '{startToken}'");

        // The lambda closes with "})" optionally followed by ".RequireAuthorization(...)" and ";".
        var close = mapperText.IndexOf("})", start, StringComparison.Ordinal);
        var end = close < 0 ? mapperText.Length : mapperText.IndexOf(';', close) + 1;
        return mapperText.Substring(start, end - start);
    }
}
