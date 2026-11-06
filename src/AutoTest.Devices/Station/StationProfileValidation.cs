using AutoTest.Contracts;
namespace AutoTest.Devices.Station;
public static class StationProfileValidation
{
    // Wiring mapping remains NOT VERIFIED. Only mock tests may provide an explicit role map.
    public static IReadOnlyList<string> Validate(StationProfile profile)
    {
        var errors=new List<string>();
        if(profile.Roles.DutPort is <0 or >1 || profile.Roles.KeypadPort is <0 or >1 || profile.Roles.DutPort==profile.Roles.KeypadPort)
            errors.Add("DUT_CAN / KEY_CAN require distinct physical 0/1 ports.");
        if(string.IsNullOrWhiteSpace(profile.VoltageProfile)) errors.Add("Voltage profile is unspecified.");
        return errors;
    }
}
