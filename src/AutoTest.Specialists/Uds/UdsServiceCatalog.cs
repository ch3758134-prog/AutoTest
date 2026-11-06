namespace AutoTest.Specialists.Uds;

public enum UdsRiskLevel { ReadOnly, DiagnosticStateChange, StateChanging, Programming }
public sealed record UdsServiceDescriptor(byte Sid, string Name, int MinimumRequestLength, UdsRiskLevel Risk);

public sealed class UdsServiceCatalog
{
    private readonly Dictionary<byte, UdsServiceDescriptor> _entries = new();
    public UdsServiceCatalog()
    {
        Register(0x10,"DiagnosticSessionControl",2,UdsRiskLevel.DiagnosticStateChange);
        Register(0x11,"ECUReset",2,UdsRiskLevel.StateChanging);
        Register(0x14,"ClearDiagnosticInformation",4,UdsRiskLevel.StateChanging);
        Register(0x19,"ReadDTCInformation",2,UdsRiskLevel.ReadOnly);
        Register(0x22,"ReadDataByIdentifier",3,UdsRiskLevel.ReadOnly);
        Register(0x23,"ReadMemoryByAddress",4,UdsRiskLevel.ReadOnly);
        Register(0x24,"ReadScalingDataByIdentifier",3,UdsRiskLevel.ReadOnly);
        Register(0x27,"SecurityAccess",2,UdsRiskLevel.Programming);
        Register(0x28,"CommunicationControl",3,UdsRiskLevel.StateChanging);
        Register(0x2A,"ReadDataByPeriodicIdentifier",3,UdsRiskLevel.ReadOnly);
        Register(0x2C,"DynamicallyDefineDataIdentifier",2,UdsRiskLevel.StateChanging);
        Register(0x2E,"WriteDataByIdentifier",4,UdsRiskLevel.StateChanging);
        Register(0x2F,"InputOutputControlByIdentifier",4,UdsRiskLevel.StateChanging);
        Register(0x31,"RoutineControl",4,UdsRiskLevel.Programming);
        Register(0x34,"RequestDownload",3,UdsRiskLevel.Programming);
        Register(0x35,"RequestUpload",3,UdsRiskLevel.Programming);
        Register(0x36,"TransferData",3,UdsRiskLevel.Programming);
        Register(0x37,"RequestTransferExit",1,UdsRiskLevel.Programming);
        Register(0x38,"RequestFileTransfer",3,UdsRiskLevel.Programming);
        Register(0x3D,"WriteMemoryByAddress",4,UdsRiskLevel.Programming);
        Register(0x3E,"TesterPresent",2,UdsRiskLevel.DiagnosticStateChange);
        Register(0x83,"AccessTimingParameter",2,UdsRiskLevel.StateChanging);
        Register(0x84,"SecuredDataTransmission",2,UdsRiskLevel.Programming);
        Register(0x85,"ControlDTCSetting",2,UdsRiskLevel.StateChanging);
        Register(0x86,"ResponseOnEvent",2,UdsRiskLevel.StateChanging);
        Register(0x87,"LinkControl",2,UdsRiskLevel.StateChanging);
    }
    public void Register(byte sid, string name, int minLength, UdsRiskLevel risk)
    {
        if (string.IsNullOrWhiteSpace(name) || minLength < 1) throw new ArgumentException("Invalid service descriptor.");
        _entries.Add(sid,new(sid,name,minLength,risk));
    }
    public UdsServiceDescriptor Get(byte sid) => _entries.TryGetValue(sid, out var result)
        ? result : throw new NotSupportedException($"UDS service 0x{sid:X2} is not in the approved catalog.");
    public UdsRiskLevel GetRiskLevel(byte sid) => Get(sid).Risk;
    public IReadOnlyCollection<UdsServiceDescriptor> All => _entries.Values.ToArray();
}
