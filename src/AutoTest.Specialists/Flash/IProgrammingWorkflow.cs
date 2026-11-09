using AutoTest.Contracts;

namespace AutoTest.Specialists.Flash;

public interface IProgrammingWorkflow
{
    ProgrammingPlan BuildPlan(ProgrammingRequest request);
    ValueTask ExecuteAsync(ProgrammingPlan plan,ProgrammingAssets assets,
        ProgrammingEvidence evidence,CancellationToken ct);
}
