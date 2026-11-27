using System.Collections.Immutable;
using AutoTest.Projects.Project;
using AutoTest.Projects.Resources;
using Xunit;

namespace AutoTest.Projects.Tests;

public sealed class SingleEcuProjectShapeTests
{
    [Fact]
    public void SecondTestProjectIsNotAValidDomainState()
    {
        var a = AssetEntry.Create(ProjectResourceKinds.TestProject, "TEST", "a.json", [1, 2]);
        var b = AssetEntry.Create(ProjectResourceKinds.TestProject, "Other TEST", "b.json", [3, 4]);
        var project = ProjectDefinition.Create("EPB") with
        { Assets = ImmutableArray.Create(a, b) };
        var error = Assert.Throws<ProjectDataException>(() => project.Validate());
        Assert.Equal("TEST_PROJECT_UNIQUE", error.Code);
    }

    [Fact]
    public void SecondDbcIsNotAValidDomainState()
    {
        var a = AssetEntry.Create(ProjectResourceKinds.Dbc, "ECU", "a.dbc", [1, 2]);
        var b = AssetEntry.Create(ProjectResourceKinds.Dbc, "Other", "b.dbc", [3, 4]);
        var project = ProjectDefinition.Create("EPB") with
        { Assets = ImmutableArray.Create(a, b) };
        var error = Assert.Throws<ProjectDataException>(() => project.Validate());
        Assert.Equal("DBC_UNIQUE", error.Code);
    }
}
