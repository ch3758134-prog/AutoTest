namespace AutoTest.Specialists.Flash;

// An in-flight UDS operation was not positively acknowledged by the target.
// For Mock jobs this means simulator state may contain partial writes; for
// future physical jobs it must never be silently retried or treated as a PASS.
public sealed class ProgrammingUncertainException : InvalidOperationException
{
    public ProgrammingUncertainException(string message) : base(message) { }
}
