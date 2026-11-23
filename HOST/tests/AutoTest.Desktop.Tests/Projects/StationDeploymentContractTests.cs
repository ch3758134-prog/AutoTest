using AutoTest.Desktop.Projects;
using Xunit;

namespace AutoTest.Desktop.Tests.Projects;

public sealed class StationDeploymentContractTests
{
    [Fact]
    public async Task InvalidDeploymentCannotOpenAnyProjectOrAuthorizeExecution()
    {
        var malformed = new StationDeploymentStore.Deployment(
            SchemaVersion: 1,
            ManifestPath: "not-a-project",
            ProjectId: Guid.Empty,
            ManifestSha256: "invalid",
            TestProjectId: Guid.Empty,
            TestProjectSha256: "invalid",
            NativeProfileSha256: "invalid",
            FlashJobFingerprint: "invalid",
            InspectionProcedureId: Guid.Empty,
            InspectionProcedureSha256: "invalid",
            DeployedAtUtc: DateTimeOffset.UtcNow);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            StationDeploymentStore.VerifyAsync(malformed, CancellationToken.None));
        Assert.Contains("部署指纹", error.Message);
    }

    [Fact]
    public async Task ValidIdsButNoManifestCannotBecomeADeployedPlan()
    {
        var nonexistent = Path.Combine(Path.GetTempPath(),
            "nonexistent-autotest-" + Guid.NewGuid().ToString("N"),
            "project.autotest-project.json");
        var sha = new string('A', 64);
        var pin = new StationDeploymentStore.Deployment(1, nonexistent,
            Guid.NewGuid(), sha, Guid.NewGuid(), sha,
            sha, sha, Guid.NewGuid(), sha, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            StationDeploymentStore.VerifyAsync(pin, CancellationToken.None));
    }
}
