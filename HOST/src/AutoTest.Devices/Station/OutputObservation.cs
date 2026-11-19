using AutoTest.Contracts;
namespace AutoTest.Devices.Station;
public static class OutputObservation
{
    public static PhysicalState ClassifyEvidence(AckKind acknowledgement,bool hasIndependentElectricalMeasurement,bool? measuredOn)
    {
        // No ACK (mock or physical) is a substitute for an independent measurement.
        if(!hasIndependentElectricalMeasurement || measuredOn is null) return PhysicalState.Unknown;
        return measuredOn.Value ? PhysicalState.VerifiedOn : PhysicalState.VerifiedOff;
    }
}
