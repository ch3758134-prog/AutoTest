using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Application.Production;

public sealed record ProductionStationReadiness(StationId Station,
    StationConnectionState Connection, string VerifiedSupply, bool ResourceLockAvailable,
    bool IsMockEnvironment, AuthorizationScope Permission);
public sealed record ProductionPreflightResult(bool Ready, ImmutableArray<string> Blockers);

public sealed class ProductionPreflight
{
    public ProductionPreflightResult ValidateMatch(MatchDecision match,
        ProductionConfiguration config)
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        if (!match.Allowed || string.IsNullOrWhiteSpace(match.DutIdentity))
            issues.Add(match.RejectionCode ?? "IDENTITY_INVALID");
        if (string.IsNullOrWhiteSpace(config.RecipeId) || config.RecipeVersion <= 0)
            issues.Add("RECIPE_INVALID");
        if (config.Assets.IsDefaultOrEmpty || !match.MatchedAssets.SequenceEqual(config.Assets))
            issues.Add("RECIPE_ASSETS_UNAVAILABLE");
        if (config.Assets.Any(a => a.Id.Value == Guid.Empty || a.Revision.Value <= 0 ||
                string.IsNullOrWhiteSpace(a.Fingerprint.Sha256)))
            issues.Add("RECIPE_ASSET_INVALID");
        return new(issues.Count == 0, issues.ToImmutable());
    }

    public ProductionPreflightResult ValidateStation(ProductionConfiguration config,
        ProductionStationReadiness station)
    {
        var issues = ImmutableArray.CreateBuilder<string>();
        if (string.IsNullOrWhiteSpace(station.Station.Value)) issues.Add("STATION_ID_MISSING");
        if (station.Connection != StationConnectionState.Ready) issues.Add("STATION_NOT_READY");
        if (!station.ResourceLockAvailable) issues.Add("STATION_BUSY");
        if (!station.IsMockEnvironment &&
            (string.IsNullOrWhiteSpace(station.VerifiedSupply) ||
                station.VerifiedSupply.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)))
            issues.Add("SUPPLY_UNVERIFIED");
        if (!string.IsNullOrWhiteSpace(config.ExpectedSupply) &&
            !config.ExpectedSupply.Equals(station.VerifiedSupply, StringComparison.OrdinalIgnoreCase))
            issues.Add("SUPPLY_MISMATCH");
        if (station.IsMockEnvironment && station.Permission != AuthorizationScope.MockOnly)
            issues.Add("MOCK_AUTH_REQUIRED");
        if (!station.IsMockEnvironment && station.Permission != AuthorizationScope.PhysicalCommand &&
            station.Permission != AuthorizationScope.Programming)
            issues.Add("PHYSICAL_AUTH_REQUIRED");
        if (config.RequiresFlash && !station.IsMockEnvironment &&
            station.Permission != AuthorizationScope.Programming)
            issues.Add("FLASH_AUTH_REQUIRED");
        return new(issues.Count == 0, issues.ToImmutable());
    }

    public ProductionPreflightResult Validate(ProductionSession session,
        ProductionConfiguration configuration, ProductionStationReadiness station)
    {
        var first = session.Match is null ? new(false, ImmutableArray.Create("MATCH_MISSING")) :
            ValidateMatch(session.Match, configuration);
        var next = ValidateStation(configuration, station);
        return new(first.Ready && next.Ready, first.Blockers.AddRange(next.Blockers));
    }
}
