using EPB.Core.Communication;
using EPB.Core.Testing;

namespace EPB.Core.Configuration;

public sealed record DefinitionBindingDiagnostic(string Code, string StepId, string Message);

public sealed class DefinitionBindingRefreshResult
{
    public IReadOnlyList<DefinitionBindingDiagnostic> Diagnostics { get; }
    public bool BlockExecution => Diagnostics.Count > 0;

    public DefinitionBindingRefreshResult(IReadOnlyList<DefinitionBindingDiagnostic> diagnostics)
        => Diagnostics = diagnostics;
}

/// <summary>
/// Core-only seam for explicit communication-database bindings.
/// A step is refreshed only when its model source is 通信数据库 and its stable
/// definition references resolve. Names are display data, never binding keys.
/// </summary>
public sealed class ConfigurationDefinitionBindingService
{
    public DefinitionBindingRefreshResult Refresh(EcuConfiguration configuration, CanDatabase database)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(database);
        // Imported databases may not yet carry project IDs. Normalize once at
        // the database boundary so stable IDs are materialized before lookup.
        database.Normalize();
        var diagnostics = new List<DefinitionBindingDiagnostic>();

        foreach (var step in EnumerateSteps(configuration))
        {
            if (step.Kind == ConfigurationStepKind.广播报文 && step.Broadcast.Source == ConfigurationSource.通信数据库)
                RefreshBroadcast(step, database, diagnostics);
            else if (step.Kind == ConfigurationStepKind.报文解析 && step.Parse.Source == ConfigurationSource.通信数据库)
                RefreshParse(step, database, diagnostics);
            else if (step.Kind == ConfigurationStepKind.波形判定 && step.WaveformAssertion.Source == ConfigurationSource.通信数据库)
                RefreshWaveformAssertion(step, database, diagnostics);
        }

        return new DefinitionBindingRefreshResult(diagnostics);
    }

    public void BindBroadcast(ConfigurationStep step, CanMessageDefinition message)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(message);
        EnsureDefinitionIds(message);
        step.Broadcast.Source = ConfigurationSource.通信数据库;
        step.Broadcast.MessageDefinitionId = message.DefinitionId;
        ApplyBroadcastDefinition(step, message);
    }

    public void BindParse(ConfigurationStep step, CanMessageDefinition message, CanSignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(signal);
        EnsureDefinitionIds(message);
        if (!message.Signals.Contains(signal))
            throw new InvalidOperationException("信号不属于所选报文定义。");

        step.Parse.Source = ConfigurationSource.通信数据库;
        step.Parse.MessageDefinitionId = message.DefinitionId;
        step.Parse.SignalDefinitionId = signal.DefinitionId;
        ApplyParseDefinition(step, message, signal);
    }

    public void BindWaveformAssertion(ConfigurationStep step, CanMessageDefinition message, CanSignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(signal);
        EnsureDefinitionIds(message);
        if (!message.Signals.Contains(signal))
            throw new InvalidOperationException("信号不属于所选报文定义。");

        step.WaveformAssertion.Source = ConfigurationSource.通信数据库;
        step.WaveformAssertion.MessageDefinitionId = message.DefinitionId;
        step.WaveformAssertion.SignalDefinitionId = signal.DefinitionId;
        ApplyWaveformAssertionDefinition(step, message, signal);
    }

    private static void RefreshBroadcast(ConfigurationStep step, CanDatabase database, ICollection<DefinitionBindingDiagnostic> diagnostics)
    {
        var resolution = new CommunicationDefinitionResolver().Resolve(step, database);
        if (!resolution.Succeeded)
        {
            foreach (var item in resolution.Diagnostics)
                diagnostics.Add(new(BindingCode(item.Status), step.Id, item.Message));
            return;
        }
        CommunicationDefinitionResolver.ApplyCanonicalProjection(step, resolution);
    }

    private static void RefreshParse(ConfigurationStep step, CanDatabase database, ICollection<DefinitionBindingDiagnostic> diagnostics)
    {
        var resolution = new CommunicationDefinitionResolver().Resolve(step, database);
        if (!resolution.Succeeded)
        {
            foreach (var item in resolution.Diagnostics)
                diagnostics.Add(new(BindingCode(item.Status), step.Id, item.Message));
            return;
        }
        CommunicationDefinitionResolver.ApplyCanonicalProjection(step, resolution);
    }

    private static void RefreshWaveformAssertion(ConfigurationStep step, CanDatabase database, ICollection<DefinitionBindingDiagnostic> diagnostics)
    {
        var resolution = new CommunicationDefinitionResolver().Resolve(step, database);
        if (!resolution.Succeeded)
        {
            foreach (var item in resolution.Diagnostics)
                diagnostics.Add(new(BindingCode(item.Status), step.Id, item.Message));
            return;
        }
        CommunicationDefinitionResolver.ApplyCanonicalProjection(step, resolution);
    }

    private static void ApplyBroadcastDefinition(ConfigurationStep step, CanMessageDefinition message)
    {
        step.Broadcast.MessageName = message.Name;
        step.Broadcast.Id = message.Id;
        step.Broadcast.Extended = message.Extended;
        step.Broadcast.Dlc = (byte)Math.Clamp(message.Dlc, 0, 64);
        // DataHex, SignalValues, Mode and PeriodMs are step runtime fields.
    }

    private static void ApplyParseDefinition(ConfigurationStep step, CanMessageDefinition message, CanSignalDefinition signal)
    {
        step.Parse.SignalName = signal.Name;
        step.Parse.DatabaseSignalName = signal.Name;
        step.Parse.DisplayName = signal.DisplayName;
        step.Parse.Id = message.Id;
        step.Parse.Extended = message.Extended;
        step.Parse.StartByte = signal.StartByte;
        step.Parse.StartBit = signal.StartBitInByte;
        step.Parse.BitLength = signal.BitLength;
        step.Parse.ByteOrder = signal.ByteOrder == CanByteOrder.Motorola ? "Motorola" : "Intel";
        step.Parse.Factor = signal.Factor;
        step.Parse.Offset = signal.Offset;
        step.Parse.Unit = signal.Unit;
        step.Parse.DisplayMode = signal.DisplayMode;
        step.Parse.AlarmType = signal.AlarmType;
        step.Parse.Description = signal.Description;
        // Expectation and TimeoutMs belong to the test-case assertion.
    }

    private static void ApplyWaveformAssertionDefinition(ConfigurationStep step, CanMessageDefinition message, CanSignalDefinition signal)
    {
        step.WaveformAssertion.SignalName = signal.Name;
        step.WaveformAssertion.DatabaseSignalName = signal.Name;
        step.WaveformAssertion.DisplayName = signal.DisplayName;
        step.WaveformAssertion.Id = message.Id;
        step.WaveformAssertion.Extended = message.Extended;
        step.WaveformAssertion.StartByte = signal.StartByte;
        step.WaveformAssertion.StartBit = signal.StartBitInByte;
        step.WaveformAssertion.BitLength = signal.BitLength;
        step.WaveformAssertion.ByteOrder = signal.ByteOrder == CanByteOrder.Motorola ? "Motorola" : "Intel";
        step.WaveformAssertion.Factor = signal.Factor;
        step.WaveformAssertion.Offset = signal.Offset;
        step.WaveformAssertion.Unit = signal.Unit;
    }

    private static DefinitionBindingDiagnostic Missing(ConfigurationStep step, string message)
        => new("DefinitionMissing", step.Id, message);

    private static string BindingCode(CommunicationDefinitionResolutionStatus status)
        => status is CommunicationDefinitionResolutionStatus.DefinitionMissing
            or CommunicationDefinitionResolutionStatus.MessageMissing
            or CommunicationDefinitionResolutionStatus.SignalMissing
            or CommunicationDefinitionResolutionStatus.SignalBelongsToAnotherMessage
            ? "DefinitionMissing"
            : status.ToString();

    private static void EnsureDefinitionIds(CanMessageDefinition message)
    {
        message.DefinitionId = string.IsNullOrWhiteSpace(message.DefinitionId)
            ? CanDatabase.StableMessageId(message.Id, message.Extended)
            : message.DefinitionId.Trim();
        foreach (var signal in message.Signals)
        {
            if (string.IsNullOrWhiteSpace(signal.Name))
                throw new InvalidOperationException("信号名称不能为空；请填写名称，别名不能替代名称。");
            signal.DefinitionId = string.IsNullOrWhiteSpace(signal.DefinitionId)
                ? CanDatabase.StableSignalId(message.DefinitionId, signal.Name)
                : signal.DefinitionId.Trim();
        }
    }

    private static IEnumerable<ConfigurationStep> EnumerateSteps(EcuConfiguration configuration)
    {
        var seen = new HashSet<ConfigurationStep>();
        foreach (var step in EnumerateSteps(configuration.Root))
            if (seen.Add(step)) yield return step;

        foreach (var testCase in configuration.TestPlan.Cases)
            foreach (var node in testCase.Flow)
                foreach (var step in EnumerateSteps(node, configuration.Root, seen))
                    yield return step;
    }

    private static IEnumerable<ConfigurationStep> EnumerateSteps(TestFlowNode node, ConfigurationTreeItem root, ISet<ConfigurationStep> seen)
    {
        if (node.Operation is not null && seen.Add(node.Operation))
            yield return node.Operation;

        foreach (var child in node.Children)
            foreach (var step in EnumerateSteps(child, root, seen))
                yield return step;

        var referenceId = !string.IsNullOrWhiteSpace(node.ResourceId) ? node.ResourceId : node.SequenceId;
        if (string.IsNullOrWhiteSpace(referenceId)) yield break;
        var sequence = FindSequence(root, referenceId);
        if (sequence is null) yield break;
        foreach (var step in sequence.Steps)
            foreach (var nested in EnumerateSteps(step))
                if (seen.Add(nested)) yield return nested;
    }

    private static ConfigurationSequence? FindSequence(ConfigurationTreeItem item, string id)
    {
        if (item.Sequence is { } sequence && (item.Id == id || sequence.Id == id)) return sequence;
        foreach (var child in item.Children)
        {
            var found = FindSequence(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<ConfigurationStep> EnumerateSteps(ConfigurationTreeItem item)
    {
        if (item.Sequence is not null)
            foreach (var step in item.Sequence.Steps)
                foreach (var nested in EnumerateSteps(step)) yield return nested;
        foreach (var child in item.Children)
            foreach (var nested in EnumerateSteps(child)) yield return nested;
    }

    private static IEnumerable<ConfigurationStep> EnumerateSteps(ConfigurationStep step)
    {
        yield return step;
        foreach (var child in step.Children)
            foreach (var nested in EnumerateSteps(child)) yield return nested;
    }
}
