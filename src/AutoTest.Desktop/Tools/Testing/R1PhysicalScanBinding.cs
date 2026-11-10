using AutoTest.Application.V1;
using System.IO;
using System.Security.Cryptography;
using AutoTest.Specialists.Scan.Lab;

namespace AutoTest.Desktop.Tools.Testing;

/// <summary>
/// Immutable identity/recipe snapshot supplied by the real serial scanner.
/// This is not a factory-release authorization. Its asset/revision values
/// must still match the current project when the physical run starts.
/// </summary>
public sealed record R1PhysicalScanBinding(
    string RawBarcode, string UnitKey, string RuleId, string CatalogRevision,
    string RecipeId, string RecipeRevision, string TestAssetId,
    int TestRevision, string TestSha256,
    string StationProfilePath, string StationProfileRevision, string StationProfileSha256,
    bool ProductionTestEnabled = false)
{
    public static R1PhysicalScanBinding FromMatch(LabMatch match, V1Workspace project)
    {
        if (match is not { Accepted: true, Identity: not null, Recipe: not null } ||
            project.ProjectPath is null || project.LabProgramAssetId is null)
            throw new InvalidOperationException("R1_SCAN_PROJECT_NOT_READY");
        var current = project.Assets.Single(x => x.Id == project.LabProgramAssetId.Value);
        var supplied = match.Recipe.TestProgram;
        if (supplied.Source != LabOrigin.Physical || !supplied.Resolved ||
            !Guid.TryParse(supplied.Key, out var testId) ||
            testId != current.Id.Value ||
            supplied.Revision != current.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            !string.Equals(supplied.Sha256, current.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R1_SCAN_TEST_ASSET_REVISION_MISMATCH");
        var station = match.Recipe.StationProfile;
        if (station.Source != LabOrigin.Physical || !station.Resolved ||
            string.IsNullOrWhiteSpace(station.Key) ||
            string.IsNullOrWhiteSpace(station.Revision) ||
            !Path.IsPathFullyQualified(station.Key) ||
            !File.Exists(station.Key))
            throw new InvalidOperationException("R1_SCAN_STATION_PROFILE_NOT_FOUND");
        var file = new FileInfo(station.Key);
        if (file.Length is <= 0 or > 4 * 1024 * 1024)
            throw new InvalidOperationException("R1_SCAN_STATION_PROFILE_INVALID");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.FullName)))
            .ToLowerInvariant();
        if (!string.Equals(hash, station.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R1_SCAN_STATION_PROFILE_CHANGED");
        return new(match.RawScan, match.Identity.UnitKey, match.Identity.RuleId,
            match.CatalogRevision, match.Recipe.RecipeId, match.Recipe.Revision,
            current.Id.Value.ToString("D"), current.Revision, current.Sha256,
            file.FullName, station.Revision, hash, match.Recipe.ProductionTestEnabled);
    }

    public void Verify(V1Workspace project)
    {
        if (project.LabProgramAssetId is null || project.ProjectPath is null)
            throw new InvalidOperationException("SCAN_PROJECT_NOT_SAVED");
        var asset = project.Assets.SingleOrDefault(x => x.Id == project.LabProgramAssetId.Value);
        if (asset is null || asset.Id.Value.ToString("D") != TestAssetId ||
            asset.Revision != TestRevision ||
            !string.Equals(asset.Sha256, TestSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R1_SCAN_TEST_BINDING_STALE");
        if (string.IsNullOrEmpty(RawBarcode) || string.IsNullOrEmpty(UnitKey) ||
            string.IsNullOrEmpty(CatalogRevision) || string.IsNullOrEmpty(RecipeId))
            throw new InvalidOperationException("R1_SCAN_IDENTITY_INCOMPLETE");
        if (!File.Exists(StationProfilePath) ||
            new FileInfo(StationProfilePath).Length is <= 0 or > 4 * 1024 * 1024 ||
            !string.Equals(Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(StationProfilePath))),
                StationProfileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("R1_SCAN_STATION_PROFILE_STALE");
    }
}
