using AutoTest.Core;

public static class MustFailToCompile
{
    public static void AcceptStation(StationId station) { }
    public static void InvalidCall() => AcceptStation(EndpointId.Create("different-type"));
}
