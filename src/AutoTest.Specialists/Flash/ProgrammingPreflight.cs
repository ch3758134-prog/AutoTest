using System.Collections.Immutable;
using AutoTest.Contracts;
using AutoTest.Core;

namespace AutoTest.Specialists.Flash;

public sealed record ProgrammingEnvironment(StationId Station, string DeviceIdentity,
    double Voltage, bool Connected, bool IsMock, long SessionGeneration = 1);
public sealed record ProgrammingAssets(ProgrammingProfile Profile,
    FirmwareImage Image, ProgrammingEnvironment Environment);

// Project/Device/integration owns asset resolution and station readings.
// This boundary is snapshot-only: CheckAsync can never send a CAN message.
public interface IProgrammingContextProvider
{
    ValueTask<ProgrammingAssets> ResolveAsync(ProgrammingRequest request,CancellationToken ct);
}

public sealed class ProgrammingPreflight(IProgrammingContextProvider provider)
{
    public async ValueTask<ProgrammingReadiness> CheckAsync(ProgrammingRequest request,CancellationToken ct)
    {
        var reasons = ImmutableArray.CreateBuilder<string>();
        try
        {
            var context = await provider.ResolveAsync(request,ct).ConfigureAwait(false);
            var profile = context.Profile; var image = context.Image; var environment = context.Environment;
            profile.Validate();
            if (!environment.Connected) reasons.Add("STATION_DISCONNECTED");
            if (!environment.IsMock) reasons.Add("PHYSICAL_FLASH_NOT_AUTHORIZED");
            if (environment.Station != request.Station) reasons.Add("STATION_MISMATCH");
            if (!string.Equals(profile.Target,request.Target,StringComparison.Ordinal)) reasons.Add("TARGET_MISMATCH");
            if (!string.Equals(profile.ExpectedDeviceIdentity,environment.DeviceIdentity,StringComparison.Ordinal))
                reasons.Add("ECU_IDENTITY_MISMATCH");
            if (!profile.Revision.Matches(request.ProfileRevision)) reasons.Add("PROFILE_REVISION_MISMATCH");
            if (!image.SourceFingerprint.Matches(request.Image.Fingerprint)) reasons.Add("IMAGE_HASH_MISMATCH");
            if (!double.IsFinite(environment.Voltage) || environment.Voltage < profile.MinVoltage
                || environment.Voltage > profile.MaxVoltage) reasons.Add("VOLTAGE_OUT_OF_RANGE");
            try { profile.ValidateImage(image); }
            catch (ArgumentException) { reasons.Add("IMAGE_ADDRESS_NOT_ALLOWED"); }
            catch (FirmwareFormatException) { reasons.Add("IMAGE_INVALID"); }
        }
        catch(OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch(Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or NotSupportedException)
        { reasons.Add("RESOURCE_UNAVAILABLE:"+ex.GetType().Name); }
        return new(reasons.Count == 0,reasons.ToImmutable());
    }
    public static void RejectMismatch(ProgrammingReadiness readiness)
    {
        if (!readiness.Ready) throw new ProgrammingRejectedException(string.Join(";",readiness.Blockers));
    }
}
public sealed class ProgrammingRejectedException : InvalidOperationException
{
    public ProgrammingRejectedException(string message) : base(message) { }
}
