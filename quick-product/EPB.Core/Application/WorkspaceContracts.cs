using EPB.Core.Configuration;
using EPB.Core.Communication;
using EPB.Core.Testing;

namespace EPB.Core.Application;

/// <summary>
/// 上位机工作区的应用层边界。WPF 只消费这些命令和状态，不直接编排配置、资源或执行细节。
/// </summary>
public interface IWorkspaceService
{
    EcuConfiguration Configuration { get; }
    bool IsDirty { get; }
    Task LoadAsync(string path, CancellationToken cancellationToken = default);
    Task SaveAsync(string? path = null, CancellationToken cancellationToken = default);
    void AddStep(TestCaseDefinition testCase, ConfigurationStep step, string? beforeStepId = null);
    bool RemoveStep(TestCaseDefinition testCase, string stepId);
    bool MoveStep(TestCaseDefinition testCase, string stepId, int offset);
}

public interface IResourceApplicationService
{
    ResourceRelationState GetState(TestFlowNode node);
    ResourceRelationResolution CopyAsLocal(TestFlowNode node);
    ResourceSaveResult SaveAsResource(ConfigurationStep step, string name);
    ResourceRelationResolution SaveAndBind(TestFlowNode node, string resourceId);
}

public interface ICommunicationApplicationService
{
    IReadOnlyList<CanMessageDefinition> GetMessages();
    IReadOnlyList<CanSignalDefinition> GetSignals(string messageDefinitionId);
    CommunicationDefinitionResolution Resolve(TestFlowNode node);
}

public interface ITestExecutionService
{
    bool IsRunning { get; }
    Task<TestPlanResult> RunPlanAsync(CancellationToken cancellationToken = default);
    Task<TestCaseResult> RunCaseAsync(string testCaseId, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    event EventHandler<TestNodeResult>? NodeCompleted;
    event EventHandler<string>? LogReceived;
}

public sealed record WorkspaceSelection(string? TestCaseId, string? NodeId, string? InspectorNodeId);

public sealed record WorkspaceState(
    string ProjectName,
    string TestCaseName,
    string TestCaseStatus,
    bool IsRunning,
    bool IsDirty,
    WorkspaceSelection Selection);
