namespace Baccarat.Core.Diagnostics;

public enum DiagnosticLevel { Information, Warning, Error }

public interface IDiagnosticSink
{
    void Write(DiagnosticLevel level, string eventName, string message);
}

public sealed class NullDiagnosticSink : IDiagnosticSink
{
    public static NullDiagnosticSink Instance { get; } = new();
    private NullDiagnosticSink() { }
    public void Write(DiagnosticLevel level, string eventName, string message) { }
}
