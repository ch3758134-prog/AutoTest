namespace AutoTest.FlashLab.Configuration;

/// <summary>
/// Values consumed by the verified CanTool F2 job builder. The source may be
/// a legacy XML import or a strongly typed native AutoTest profile. A native
/// profile MUST NOT be serialized to temporary XML for production execution.
/// </summary>
public interface IFlashParameterSource
{
    string Get(string name);
    (uint Request, uint Response) GetEndpoint(bool boot);
    void ValidateConfiguredRange();
    IEnumerable<KeyValuePair<string, string>> FingerprintEntries();
}
