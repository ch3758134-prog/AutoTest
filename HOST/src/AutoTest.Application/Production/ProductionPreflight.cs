using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

public sealed record ProductionStationReadiness(StationId Station,
    StationConnectionState Connection, string VerifiedSupply, bool ResourceLockAvailable,
    long ControllerEpoch, AuthorizationScope Permission);
public sealed record ProductionPreflightResult(bool Ready, ImmutableArray<string> Blockers);

public sealed class ProductionPreflight
{
    public ProductionPreflightResult ValidateMatch(MatchDecision match,
        ProductionProjectExecution config)
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        if (!match.Allowed || string.IsNullOrWhiteSpace(match.DutIdentity))
            issues.Add(match.RejectionCode ?? "IDENTITY_INVALID");
        if (config.Project.Value == Guid.Empty || config.Revision.Value <= 0 ||
            config.PackageFingerprint.Sha256 is not { Length: 64 })
            issues.Add("PROJECT_VERSION_INVALID");
        if (config.Assets.IsDefaultOrEmpty || !match.MatchedAssets.SequenceEqual(config.Assets))
            issues.Add("PROJECT_ASSETS_UNAVAILABLE");
        if (config.Assets.Any(a => a.Id.Value == Guid.Empty || a.Revision.Value <= 0 ||
                string.IsNullOrWhiteSpace(a.Fingerprint.Sha256)))
            issues.Add("PROJECT_ASSET_INVALID");
        return new(issues.Count == 0, issues.ToImmutable());
    }

    public ProductionPreflightResult ValidateStation(ProductionProjectExecution config,
        ProductionStationReadiness station)
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        if (string.IsNullOrWhiteSpace(station.Station.Value)) issues.Add("STATION_ID_MISSING");
        if (station.Connection != StationConnectionState.Ready) issues.Add("STATION_NOT_READY");
        if (!station.ResourceLockAvailable) issues.Add("STATION_BUSY");
        if (station.ControllerEpoch <= 0)
            issues.Add("CONTROLLER_SESSION_NOT_BOUND");
        if (string.IsNullOrWhiteSpace(station.VerifiedSupply) ||
            station.VerifiedSupply.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            issues.Add("SUPPLY_UNVERIFIED");
        if (!string.IsNullOrWhiteSpace(config.ExpectedSupply) &&
            !config.ExpectedSupply.Equals(station.VerifiedSupply,
                StringComparison.OrdinalIgnoreCase))
            issues.Add("SUPPLY_MISMATCH");
        if (!config.RequiresFlash)
            issues.Add("PRODUCTION_FLASH_REQUIRED");
        if (station.Permission != AuthorizationScope.Programming)
            issues.Add("FLASH_AUTH_REQUIRED");
        return new(issues.Count == 0, issues.ToImmutable());
    }

    public ProductionPreflightResult Validate(ProductionSession session,
        ProductionProjectExecution configuration, ProductionStationReadiness station)
    {
        var first = session.Match is null ? new(false, ImmutableArray.Create("MATCH_MISSING")) :
            ValidateMatch(session.Match, configuration);
        var next = ValidateStation(configuration, station);
        return new(first.Ready && next.Ready, first.Blockers.AddRange(next.Blockers));
    }
}
