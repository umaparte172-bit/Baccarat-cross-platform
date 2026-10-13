using System.Text.RegularExpressions;
using Baccarat.Core.Diagnostics;

namespace Baccarat.Infrastructure.Diagnostics;

public sealed partial class RedactingDiagnosticSink : IDiagnosticSink
{
    private readonly Action<string> writer;

    public RedactingDiagnosticSink(Action<string> writer) =>
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));

    public void Write(DiagnosticLevel level, string eventName, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        writer($"{level}|{eventName}|{Redact(message ?? string.Empty)}");
    }

    public static string Redact(string value)
    {
        var redacted = BearerToken().Replace(value, "$1[REDACTED]");
        redacted = SensitiveAssignment().Replace(redacted, "$1[REDACTED]");
        return EmailAddress().Replace(redacted, "[REDACTED_EMAIL]");
    }

    [GeneratedRegex("(?i)(bearer\\s+)[A-Za-z0-9._~+\\-/]+=*")]
    private static partial Regex BearerToken();

    [GeneratedRegex("(?i)(password|token|secret|api[_-]?key)\\s*[:=]\\s*[^,;\\s]+")]
    private static partial Regex SensitiveAssignment();

    [GeneratedRegex("(?i)\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b")]
    private static partial Regex EmailAddress();
}
