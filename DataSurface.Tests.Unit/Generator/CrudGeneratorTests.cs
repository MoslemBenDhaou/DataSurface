using System;
using System.IO;
using System.Linq;
using DataSurface.Core.Annotations;
using DataSurface.Generator;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DataSurface.Tests.Unit.Generator;

/// <summary>
/// Drives <see cref="CrudGenerator"/> over an in-memory compilation and inspects the emitted
/// source. Verifies B4: generated CRUD routes enforce the resource's [CrudAuthorize] policy and
/// bind the raw JSON body for PATCH (sparse semantics) instead of serializing a typed DTO.
/// </summary>
public class CrudGeneratorTests
{
    private const string Source = @"
using DataSurface.Core.Annotations;
using DataSurface.Core.Enums;

namespace TestApp;

[CrudResource(""widgets"")]
[CrudAuthorize(""WidgetAdmin"")]
public class Widget
{
    [CrudKey]
    public int Id { get; set; }

    [CrudField(CrudDto.Read | CrudDto.Create | CrudDto.Update)]
    public string Name { get; set; } = """";
}
";

    private static string RunGenerator(string source)
    {
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .Append(MetadataReference.CreateFromFile(typeof(CrudResourceAttribute).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "TestAsm",
            new[] { CSharpSyntaxTree.ParseText(source) },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new CrudGenerator().AsSourceGenerator());
        driver = driver.RunGenerators(compilation);

        return string.Join("\n", driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()));
    }

    [Fact]
    public void GeneratedEndpoints_EnforceAuthorizationPolicy_AndBindSparsePatch()
    {
        var generated = RunGenerator(Source);

        // Sanity: the endpoint mapper was emitted.
        generated.Should().Contain("MapDataSurfaceGeneratedCrud");

        // B4 (auth): [CrudAuthorize("WidgetAdmin")] (no Operation => all ops) must be applied to
        // the generated routes, which previously bypassed authorization entirely.
        generated.Should().Contain(".RequireAuthorization(\"WidgetAdmin\")");

        // B4 (sparse PATCH): the PATCH handler binds the raw JSON body so an omitted property is
        // distinguishable from an explicit null, instead of serializing a fully-populated DTO.
        generated.Should().Contain("JsonObject patch");
        generated.Should().NotContain("SerializeToNode(patch)");
    }
}
